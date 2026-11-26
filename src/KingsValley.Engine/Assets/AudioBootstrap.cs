#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxAudio.Songs;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Extracts cartridge content through independent audio and graphics lifecycles, so the distributed build carries
/// no Konami content.
/// </summary>
/// <remarks>
/// <para>The build ships only code: the <c>.msxaudio</c> package is produced on the player's machine
/// from the cartridge they own, using the cartridge's own sound driver (<see
/// cref="DirectRomExtractor"/>, no external emulator).</para>
///
/// <para><b>Where it goes:</b> <c>&lt;MSXMusic&gt;/kings-valley-2/&lt;msx1|msx2&gt;/Audio/audio.msxaudio</c> — the shared
/// <c>MSXMusic</c> folder (see <see cref="MsxAudioPaths"/>), the same one the MSXScope player uses, so a
/// machine with several games keeps every extraction in one place. Next to the package there is a small JSON
/// remembering which ROM produced it.</para>
///
/// <para><b>Cost:</b> the extraction runs the driver for every id of the profile, so it takes
/// minutes. It runs on a background thread with progress and can be cancelled; while it runs the game
/// keeps playing (silent) and starts using the audio as soon as it finishes
/// (<see cref="Completed"/>).</para>
/// </remarks>
public static class AudioBootstrap
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? _cts;
    private static Task<ExtractionResult?>? _task;
    private static double _progress;
    private static string _status = string.Empty;
    private static string? _lastReportPath;

    /// <summary>Offline end-user report generated for the most recent extraction attempt.</summary>
    public static string? LastReportPath { get { lock (Gate) return _lastReportPath; } }

    /// <summary>
    /// Test-only override for <see cref="ExtractedDirectory"/>. Production always uses the shared
    /// <c>MSXMusic</c> folder; tests point this at a temporary folder and restore it to <c>null</c> afterwards.
    /// </summary>
    public static string? ExtractedDirectoryOverrideForTests { get; set; }

    /// <summary>Collection root shared with MSXScope. Test runs use an isolated temporary root.</summary>
    internal static string CollectionRoot => ExtractedDirectoryOverrideForTests ?? MsxAudioPaths.Home;

    public static string GraphicsRootFor(ArtFlavor flavor)=>GraphicsRootForVariant(CartridgeVariants.Original(flavor));
    public static string GraphicsRootForVariant(string variant)=>Wagenheimer.MsxGraphics.Extraction.GraphicsLayout.GetRoot(CollectionRoot,"kings-valley-2",variant);
    public static string GraphicsDirectoryFor(ArtFlavor flavor)=>GraphicsDirectoryForVariant(CartridgeVariants.Original(flavor));
    public static string GraphicsDirectoryForVariant(string variant)
    {
        string root=GraphicsRootForVariant(variant);
        return GraphicsGenerationStore.Current(root,variant) ?? Path.Combine(root,"unavailable");
    }
    public static string ExtractedDirectory=>GraphicsDirectoryForVariant(RomContent.Variant);
    public static string? LastGraphicsAttemptDirectory { get; private set; }
    public static string ExtractedPackagePath=>AudioPackageForVariant(RomContent.Variant);
    internal static string AudioPackageFor(ArtFlavor flavor)=>AudioPackageForVariant(CartridgeVariants.Original(flavor));
    internal static string AudioPackageForVariant(string variant)=>MsxAudioPaths.PackagePath("kings-valley-2",variant,CollectionRoot);

    private static readonly Dictionary<string, (DateTime Stamp, long Size, bool Valid)> AudioValidation = new();

    private static bool IsCurrentAudio(string path, string variant)
    {
        if (!File.Exists(path)) return false;
        var info = new FileInfo(path);
        lock (Gate)
        {
            if (AudioValidation.TryGetValue(path, out var cached) && cached.Stamp == info.LastWriteTimeUtc && cached.Size == info.Length)
                return cached.Valid;
            bool valid = System.Linq.Enumerable.Any(RomExtractionProfiles.FindAllByGameVariant("kings-valley-2",variant),
                profile => RomExtractionProfiles.IsCurrentPackage(path, profile));
            AudioValidation[path] = (info.LastWriteTimeUtc, info.Length, valid);
            return valid;
        }
    }

    /// <summary>Package that ships with a development build (inside <c>Content</c>).</summary>
    public static string ContentPackagePath => Path.Combine(RomContent.AudioDirectory, "audio.msxaudio");

    /// <summary>Is there a package to play (extracted first, then the development one)?</summary>
    public static bool HasPackageForVariant(string variant)=>IsCurrentAudio(AudioPackageForVariant(variant),variant);

    public static bool HasPackageFor(ArtFlavor flavor) => IsCurrentAudio(AudioPackageFor(flavor),CartridgeVariants.Original(flavor));

    public static bool HasPackage => IsCurrentAudio(ExtractedPackagePath, RomContent.Variant);

    /// <summary>Path of the package to load.</summary>
    public static string? PackagePath => HasPackage ? ExtractedPackagePath : null;

    /// <summary>Is an extraction running?</summary>
    public static bool IsRunning
    {
        get
        {
            lock (Gate) return _task is { IsCompleted: false };
        }
    }

    /// <summary>Progress of the running extraction (0..1).</summary>
    public static double Progress
    {
        get
        {
            lock (Gate) return _progress;
        }
    }

    /// <summary>Last status line (song/effect being captured, or an error).</summary>
    public static string Status
    {
        get
        {
            lock (Gate) return _status;
        }
    }

    /// <summary>Raised on the background thread when the extraction ends (success or not).</summary>
    public static event Action<bool>? Completed;

    /// <summary>
    /// Starts the extraction for <paramref name="romPath"/>. Returns false when it cannot start
    /// (missing ROM, no profile, already running).
    /// </summary>
    /// <param name="romPath">Cartridge image (a <c>.rom</c> or a <c>.zip</c>).</param>
    /// <param name="error">Reason when it returns false.</param>
    public static bool Start(string romPath, out string? error)
    {
        error = null;

        if (IsRunning)
        {
            error = "extraction already running";
            return false;
        }

        if (!File.Exists(romPath))
        {
            error = $"ROM not found: {romPath}";
            return false;
        }

        // Audio and Stage extraction routines support both MSX1 (RC-760) and MSX2 (RC-761).

        RomExtractionProfile profile;
        try { profile = LoadProfile(File.ReadAllBytes(RomFile.Resolve(romPath))); }
        catch (Exception ex) { error = ex.Message; return false; }
        var options = new RomExtractionOptions { RomPath = romPath, Profile = profile };

        var cts = new CancellationTokenSource();

        lock (Gate)
        {
            if (_task is {IsCompleted:false}) { cts.Dispose(); error="extraction already running"; return false; }
            _cts = cts;
            _lastReportPath = null;
            LastGraphicsAttemptDirectory = null;
            _progress = 0;
            _status = "starting...";
            _task = Task.Run(() => Run(options, cts.Token));
        }

        return true;
    }

    /// <summary>Asks the running extraction to stop.</summary>
    public static void Cancel()
    {
        lock (Gate) _cts?.Cancel();
    }

    /// <summary>Cooperative shutdown: lets the worker retain its attempt and finish the offline report.</summary>
    public static bool CancelAndWait(TimeSpan timeout)
    {
        Task? running;
        lock(Gate) { _cts?.Cancel(); running=_task; }
        if(running==null) return true;
        if(Task.CurrentId==running.Id) return false;
        try { return running.Wait(timeout); }
        catch(AggregateException error)
        {
            Console.WriteLine("[AudioBootstrap] extraction stopped with an error: "+error.GetBaseException().GetType().Name);
            return true;
        }
    }

    /// <summary>
    /// First-run entry point: when there is no package, extracts it from the player's ROM.
    /// </summary>
    /// <remarks>
    /// <para>Only the environment variable is handled here (<c>KINGSVALLEY_ROM=&lt;path&gt;</c>, used by
    /// automation and tests). Without it the game opens the first-run screen, which explains what is
    /// needed and browses the disk — no OS dialog.</para>
    /// <para><c>KINGSVALLEY_EXTRACT_IDS=3,5,19</c> limits the ids (quick tests).</para>
    /// </remarks>
    public static void AutoStart()
    {
        if (HasPackage && RomContent.IsVariantAvailable(RomContent.Variant))
        {
            Console.WriteLine($"[AudioBootstrap] package: {PackagePath}");
            return;
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable("KINGSVALLEY_ROM");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            StartFrom(fromEnvironment);
            return;
        }

        string desired = RomContent.Variant;
        foreach (var candidate in RomFolderManager.ScanRoms())
        {
            try
            {
                byte[] data = File.ReadAllBytes(RomFile.Resolve(candidate.FilePath));
                var profile = RomExtractionProfiles.FindForRom(data);
                if (profile?.GameId == "kings-valley-2" && profile.Variant == desired)
                {
                    StartFrom(candidate.FilePath);
                    return;
                }
            }
            catch (Exception ex) { Console.WriteLine($"[AudioBootstrap] cartridge probe: {ex.Message}"); }
        }
        Console.WriteLine("[AudioBootstrap] no current audio package: the first-run screen will ask for the ROM.");
    }

    /// <summary>Shows the ROM dialog (does nothing when one is already running).</summary>
    public static void PromptForRom()
    {
        if (IsRunning)
            return;

        Console.WriteLine("[AudioBootstrap] no audio package: asking for the cartridge.");

        if (!Platform.RomPicker.IsAvailable)
        {
            const string message =
                "No file dialog is available on this system.\n\n" +
                "Drag the King's Valley II cartridge (.rom or .zip) onto the game window, " +
                "or start the game with KINGSVALLEY_ROM=<path>.";

            Console.WriteLine("[AudioBootstrap] no file dialog on this system — drop the .rom/.zip on " +
                              "the window or set KINGSVALLEY_ROM=<path>.");
            Platform.SdlMessageBox.Show("King's Valley II — cartridge needed", message, warning: true);
            return;
        }

        string? path = Platform.RomPicker.Pick();
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("[AudioBootstrap] cancelled — the game runs without audio; F12 asks again.");
            return;
        }

        StartFrom(path);
    }

    private static void StartFrom(string romPath)
    {
        if (!Start(romPath, out string? error))
        {
            Console.WriteLine($"[AudioBootstrap] cannot start: {error}");
            return;
        }

        Console.WriteLine($"[AudioBootstrap] extracting cartridge content from {Path.GetFileName(romPath)} " +
                          "(runs in the background; existing current audio is reused).");
    }

    /// <summary>
    /// Accepts a cartridge dropped on the window — the fallback when the system has no file dialog
    /// (any platform, including a Linux desktop without zenity/kdialog).
    /// </summary>
    /// <returns>True when the drop started an extraction.</returns>
    public static bool AcceptDrop(string path)
    {
        if (IsRunning || !File.Exists(path))
            return false;

        string extension = Path.GetExtension(path);
        if (!extension.Equals(".rom", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return false;

        Console.WriteLine($"[AudioBootstrap] cartridge dropped: {Path.GetFileName(path)}");
        StartFrom(path);
        return true;
    }

    private static ExtractionResult? Run(RomExtractionOptions options, CancellationToken token)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        byte[]? reportRom = null;
        string? reportAudioPath = null;
        string outcome = "failed";
        string? failure = null;
        string? reportGraphicsDirectory = null;
        string sourceName = Path.GetFileName(options.RomPath);
        string? preparationNotice = null;
        string? pendingAudioPath = null;
        try
        {


            string resolved = RomFile.Resolve(options.RomPath);
            string prepared = ContestEnglishPatch.Prepare(resolved, CollectionRoot,
                line => { lock (Gate) _status = line; }, token);
            byte[] rom = File.ReadAllBytes(prepared);
            if (prepared != resolved)
                preparationNotice = $"English translation by FRS and contributors (enhancement patch v1.1). Original cartridge preserved: SHA-1 {Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(resolved))).ToLowerInvariant()}. Working cartridge SHA-1 {Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(rom)).ToLowerInvariant()}. Complete original patch archive and license are bundled. Source: {ContestEnglishPatch.ArchiveUrl}";
            options.RomPath = prepared;
            options.Profile = LoadProfile(rom);
            reportRom = rom;
            string audioPath = AudioPackageForVariant(options.Profile.Variant!);
            reportAudioPath = audioPath;

            // 1. Audio extraction (0% .. 40%) - only runs if package does not already exist
            ExtractionResult? result = null;
            if (!RomExtractionProfiles.IsCurrentPackage(audioPath, options.Profile, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(rom))))
            {
                int total = Math.Max(1, options.Profile.Ids.Count);

                result = DirectRomExtractor.Extract(
                    options,
                    log: line =>
                    {
                        lock (Gate) _status = line;
                        Console.WriteLine($"[AudioBootstrap] {line}");
                    },
                    progress: (index, count) =>
                    {
                        lock (Gate) _progress = Math.Min(0.40, 0.40 * index / (double)Math.Max(1, count));
                    }, cancellationToken: token);

                token.ThrowIfCancellationRequested();
                string attemptDirectory = Path.Combine(Path.GetDirectoryName(audioPath)!, "attempts", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(attemptDirectory);
                pendingAudioPath = Path.Combine(attemptDirectory, "audio.pending");
                result.Library.Save(pendingAudioPath);
                reportAudioPath = pendingAudioPath;
                File.WriteAllText(Path.Combine(attemptDirectory, "capture-report.json"),
                    JsonSerializer.Serialize(result.Diagnostics, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                lock (Gate)
                {
                    _status = "audio already extracted [OK]";
                    _progress = 0.40;
                }
            }

            token.ThrowIfCancellationRequested();

            // Graphics has an independent identity, staging area, inventory and publication lifecycle.
            int stageLimit = int.TryParse(Environment.GetEnvironmentVariable("KINGSVALLEY_EXTRACT_STAGES"), out int limit) ? limit : CartridgeVariants.StageCount(options.Profile.Variant!);
            var context = GraphicsExtractionContext.Create(rom, options.Profile.Variant!, CollectionRoot, stageLimit);
            var graphics = GraphicsExtractionCoordinator.Extract(context, line =>
            {
                lock (Gate) _status = line;
            }, token, progress => { lock (Gate) _progress=0.40+0.60*progress; });
            reportGraphicsDirectory = graphics.Directory;
            LastGraphicsAttemptDirectory = graphics.Directory;
            outcome = graphics.Manifest.Outcome;
            if (graphics.Manifest.Ready)
            {
                // A failed graphics attempt must not replace another revision's playable audio.
                if (pendingAudioPath != null)
                {
                    File.Move(pendingAudioPath, audioPath, true);
                    File.Move(Path.Combine(Path.GetDirectoryName(pendingAudioPath)!, "capture-report.json"),
                        Path.Combine(Path.GetDirectoryName(audioPath)!, "capture-report.json"), true);
                    reportAudioPath = audioPath;
                }
                GameAssets.Invalidate();
            }
            lock (Gate)
            {
                _progress = 1;
                _status = graphics.Manifest.Ready ? "Extraction complete" : $"Graphics {outcome}; see the extraction report for missing components";
            }

            Console.WriteLine($"[AudioBootstrap] attempt {outcome}: {_status} -> {audioPath}");
            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            lock (Gate) _status = "cancelled";
            Console.WriteLine("[AudioBootstrap] extraction cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            failure = Wagenheimer.MsxGraphics.Extraction.ExtractionError.Describe(ex);
            lock (Gate) _status = failure;
            Console.WriteLine($"[AudioBootstrap] extraction failed: {ex}");
            return null;
        }
        finally
        {
            // Always write the end-user report, including partial/failed attempts. No developer
            // environment variable and no automatic browser launch are required.
            try
            {
                string variant=options.Profile.Variant!;
                string package = reportAudioPath ?? AudioPackageForVariant(variant);
                string reportDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(AudioPackageForVariant(variant))!)!, "Report");
                string hash = reportRom == null ? string.Empty : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(reportRom)).ToLowerInvariant();
                string reportPath = ExtractionReportBuilder.Build(new(reportGraphicsDirectory ?? GraphicsDirectoryForVariant(variant), package, reportDirectory,
                    sourceName, hash, options.Profile.Variant ?? "unknown", outcome,
                    elapsed.Elapsed.TotalSeconds, failure, preparationNotice), line => Console.WriteLine("[ExtractionReport] " + line));
                lock (Gate) _lastReportPath = reportPath;
            }
            catch (Exception reportError)
            {
                Console.WriteLine("[ExtractionReport] could not write report: " + reportError.Message);
            }
            try { Completed?.Invoke(outcome == "completed"); }
            finally
            {
                lock (Gate)
                {
                    _task = null;
                    _cts?.Dispose();
                    _cts = null;
                }
            }
        }
    }

    /// <summary>Game-specific selection of the reusable library's curated cartridge profile.</summary>
    internal static RomExtractionProfile LoadProfile(byte[] rom)
    {
        if (Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(rom)).Equals(SupportedCartridges.ConvertedIoSha1, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This is the known I/O-mapped conversion, with reordered banks and no standard AB cartridge header. Its loader and custom port mapping are not implemented yet. Use a verified original cartridge revision; renaming this file will not change its format.");
        RomExtractionProfile profile = RomExtractionProfiles.FindForRom(rom)
            ?? throw new InvalidDataException("This cartridge revision is not supported yet (SHA-1 " +
                Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(rom)) +
                "). Its sound driver must be verified before extraction.");
        if (profile.GameId != "kings-valley-2") throw new InvalidDataException("Not a King's Valley II audio profile.");

        // Quick tests: "KINGSVALLEY_EXTRACT_IDS=3,5,19" limits the work (the full profile is minutes).
        string? filter = Environment.GetEnvironmentVariable("KINGSVALLEY_EXTRACT_IDS");
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var ids = new List<int>();
            foreach (string part in filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out int id))
                    ids.Add(id);
            }

            if (ids.Count > 0)
            {
                Console.WriteLine($"[AudioBootstrap] id filter: {string.Join(", ", ids)}");
                profile.Ids = ids;
                profile.PartialExtraction = true;
            }
        }

        return profile;
    }

}
