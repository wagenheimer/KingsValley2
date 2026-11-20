#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Extracts the game's audio **from the player's ROM** on first run, so the distributed build carries
/// no Konami content.
/// </summary>
/// <remarks>
/// <para>The build ships only code: the <c>.msxaudio</c> package is produced on the player's machine
/// from the cartridge they own, using the cartridge's own sound driver (<see
/// cref="DirectRomExtractor"/>, no external emulator).</para>
///
/// <para><b>Where it goes:</b> <c>%APPDATA%\KingsValley\extracted\audio.msxaudio</c> (the install
/// folder may be read-only), next to a small JSON remembering which ROM produced it.</para>
///
/// <para><b>Cost:</b> the extraction runs the driver for every id of the profile, so it takes
/// minutes. It runs on a background thread with progress and can be cancelled; while it runs the game
/// keeps playing (silent) and starts using the audio as soon as it finishes
/// (<see cref="Completed"/>).</para>
/// </remarks>
public static class AudioBootstrap
{
    /// <summary>Profile shipped with the game (addresses and ids only — no ROM data).</summary>
    private const string ProfileResource = "KingsValley.Engine.Assets.kingsvalley2.json";

    private static readonly object Gate = new();
    private static CancellationTokenSource? _cts;
    private static Task<ExtractionResult?>? _task;
    private static double _progress;
    private static string _status = string.Empty;

    /// <summary>
    /// Test-only override for <see cref="ExtractedDirectory"/>. Production always uses AppData; tests point
    /// this at a temporary folder and restore it to <c>null</c> afterwards.
    /// </summary>
    public static string? ExtractedDirectoryOverrideForTests { get; set; }

    /// <summary>Folder with the extracted content (created on demand).</summary>
    public static string ExtractedDirectory => !string.IsNullOrWhiteSpace(ExtractedDirectoryOverrideForTests)
        ? ExtractedDirectoryOverrideForTests!
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley", "extracted");

    /// <summary>Package produced by the extraction.</summary>
    public static string ExtractedPackagePath => Path.Combine(ExtractedDirectory, "audio.msxaudio");

    /// <summary>Package that ships with a development build (inside <c>Content</c>).</summary>
    public static string ContentPackagePath => Path.Combine(RomContent.AudioDirectory, "audio.msxaudio");

    /// <summary>Is there a package to play (extracted first, then the development one)?</summary>
    public static bool HasPackage => File.Exists(ExtractedPackagePath) || File.Exists(ContentPackagePath);

    /// <summary>Path of the package to load.</summary>
    public static string? PackagePath =>
        File.Exists(ExtractedPackagePath) ? ExtractedPackagePath
        : File.Exists(ContentPackagePath) ? ContentPackagePath
        : null;

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

        // Validate that this ROM is MSX1 (RC-760). Audio and Stage extraction routines require MSX1.
        var scannedInfo = RomFolderManager.InspectRomFile(romPath);
        if (scannedInfo != null && scannedInfo.Flavor == ScannedRomFlavor.Msx2)
        {
            error = "AUDIO & STAGES REQUIRE MSX1 RC-760. SELECT THE MSX1 ROM.";
            return false;
        }

        RomExtractionProfile profile = LoadProfile();
        var options = new RomExtractionOptions { RomPath = romPath, Profile = profile };

        var cts = new CancellationTokenSource();

        lock (Gate)
        {
            _cts = cts;
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
        if (HasPackage)
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

        Console.WriteLine("[AudioBootstrap] no audio package: the first-run screen will ask for the ROM.");
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

        Console.WriteLine($"[AudioBootstrap] extracting the audio from {Path.GetFileName(romPath)} " +
                          "(runs in the background; the game keeps playing).");
    }

    /// <summary>
    /// Accepts a cartridge dropped on the window — the fallback when the system has no file dialog
    /// (any platform, including a Linux desktop without zenity/kdialog).
    /// </summary>
    /// <returns>True when the drop started an extraction.</returns>
    public static bool AcceptDrop(string path)
    {
        if (HasPackage || IsRunning || !File.Exists(path))
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
        try
        {
            Directory.CreateDirectory(ExtractedDirectory);

            int total = Math.Max(1, options.Profile.Ids.Count);

            var result = DirectRomExtractor.Extract(
                options,
                log: line =>
                {
                    lock (Gate) _status = line;
                    Console.WriteLine($"[AudioBootstrap] {line}");
                },
                progress: (index, count) =>
                {
                    // Audio extraction covers 0% .. 40% of overall bootstrap progress
                    lock (Gate) _progress = Math.Min(0.40, 0.40 * index / (double)Math.Max(1, count));
                });

            token.ThrowIfCancellationRequested();

            byte[] rom = File.ReadAllBytes(options.RomPath);

            // The levels come from the same cartridge: write them right after (40% .. 60%)
            int stageLimit = int.TryParse(Environment.GetEnvironmentVariable("KINGSVALLEY_EXTRACT_STAGES"), out int sLimit) ? sLimit : 60;
            RomStageJson.WriteAll(rom,
                Path.Combine(ExtractedDirectory, "Levels"),
                (stage, total) =>
                {
                    lock (Gate)
                    {
                        _status = $"level {stage}/{total}";
                        _progress = 0.40 + 0.20 * (stage / (double)Math.Max(1, total));
                    }
                },
                maxStages: stageLimit);

            token.ThrowIfCancellationRequested();

            // The fixed screens come from the same run of the cartridge: (60% .. 75%)
            lock (Gate)
            {
                _status = "screens & palettes";
                _progress = 0.60;
            }
            RomScreens.WriteAll(rom, ExtractedDirectory,
                line =>
                {
                    lock (Gate)
                    {
                        _status = line;
                        _progress = Math.Min(0.75, _progress + 0.02);
                    }
                });

            token.ThrowIfCancellationRequested();

            // MSX1 gameplay rooms are SCREEN 2 dumps, never PNGs (75% .. 98%)
            try
            {
                lock (Gate)
                {
                    _status = "rooms & murals";
                    _progress = 0.75;
                }
                int roomStages = int.TryParse(Environment.GetEnvironmentVariable("KINGSVALLEY_EXTRACT_STAGES"), out int rLimit) ? rLimit : 60;
                RomRoomDumps.WriteAll(rom, Path.Combine(ExtractedDirectory, "Levels"), ExtractedDirectory,
                    line =>
                    {
                        lock (Gate)
                        {
                            _status = line;
                            if (line.StartsWith("room ", StringComparison.OrdinalIgnoreCase))
                            {
                                // Parse room progress if possible
                                _progress = Math.Min(0.98, _progress + 0.003);
                            }
                        }
                    }, token, stages: roomStages);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception roomError)
            {
                Warn("room dumps", roomError);
            }

            token.ThrowIfCancellationRequested();
            lock (Gate) _progress = 1.0;

            // Write to a temp name and move, so an interrupted run never leaves a broken package.
            string temp = ExtractedPackagePath + ".tmp";
            result.Library.Save(temp);
            File.Move(temp, ExtractedPackagePath, overwrite: true);

            WriteStamp(options.RomPath, result.Library.Songs.Count, result.Library.Clips.Count);

            token.ThrowIfCancellationRequested();

            // Gallery + browser player (none of it is used by the game). Runs after the package
            // exists so it can render the songs; a failure here does not invalidate the extraction.
            if (!string.Equals(Environment.GetEnvironmentVariable("KINGSVALLEY_SKIP_PREVIEW"), "1",
                    StringComparison.Ordinal))
            {
                try
                {
                    lock (Gate) _status = "report";
                    ExtractionPreview.Build(ExtractedDirectory, ExtractedPackagePath, rom,
                        line => { lock (Gate) _status = line; });
                }
                catch (Exception previewError)
                {
                    Console.WriteLine($"[AudioBootstrap] preview failed: {previewError}");
                }
            }

            token.ThrowIfCancellationRequested();

            // The 14 captured poses alone are not enough for the renderer. This writes every name the game
            // asks for (player sets, enemies, coffins, doors, items, font), using real poses where available
            // and simple fallback shapes elsewhere. It never overwrites a real MSX2-derived atlas.
            try
            {
                lock (Gate) _status = "atlas";
                RomSpriteAtlas.Build(rom, ExtractedDirectory,
                    line => { lock (Gate) _status = line; }, token: token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception atlasError)
            {
                Warn("sprite atlas", atlasError);
            }

            // Atlases are cached globally. Invalidate them from this background thread; the dispose and
            // reload happen on the main thread at the next atlas access (GraphicsDevice is not thread-safe).
            try
            {
                GameAssets.Invalidate();
            }
            catch (Exception reloadError)
            {
                Warn("asset reload", reloadError);
            }

            lock (Gate)
            {
                _progress = 1;
                _status = $"{result.Library.Songs.Count} songs, {result.Library.Clips.Count} effects";
            }

            Console.WriteLine($"[AudioBootstrap] extracted {_status} -> {ExtractedPackagePath}");
            Completed?.Invoke(true);
            return result;
        }
        catch (OperationCanceledException)
        {
            lock (Gate) _status = "cancelled";
            Console.WriteLine("[AudioBootstrap] extraction cancelled.");
            Completed?.Invoke(false);
            return null;
        }
        catch (Exception ex)
        {
            lock (Gate) _status = ex.Message;
            Console.WriteLine($"[AudioBootstrap] extraction failed: {ex}");
            Completed?.Invoke(false);
            return null;
        }
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

    /// <summary>Reports a non-fatal extraction step without losing the final "songs, effects" status.</summary>
    private static void Warn(string step, Exception error)
    {
        string message = $"warning: {step} failed: {error.Message}";
        Console.WriteLine($"[AudioBootstrap] {message}");
        lock (Gate)
        {
            if (_status != message)
                _status = message;
        }
    }

    /// <summary>The profile embedded in the assembly (addresses and ids of the driver).</summary>
    private static RomExtractionProfile LoadProfile()
    {
        Assembly assembly = typeof(AudioBootstrap).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ProfileResource)
                               ?? throw new InvalidOperationException(
                                   $"extraction profile '{ProfileResource}' not embedded");
        using var reader = new StreamReader(stream);
        RomExtractionProfile profile = JsonSerializer.Deserialize<RomExtractionProfile>(
                   reader.ReadToEnd(),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("invalid extraction profile");

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
            }
        }

        return profile;
    }

    /// <summary>Remembers which ROM produced the package (and re-extracts when it changes).</summary>
    private static void WriteStamp(string romPath, int songs, int clips)
    {
        try
        {
            var info = new FileInfo(romPath);
            string hash;
            using (var sha = SHA1.Create())
            using (var stream = File.OpenRead(romPath))
                hash = Convert.ToHexString(sha.ComputeHash(stream));

            var stamp = new
            {
                rom = info.Name,
                bytes = info.Length,
                sha1 = hash,
                songs,
                clips,
                extractedAt = DateTime.UtcNow.ToString("O"),
            };

            File.WriteAllText(Path.Combine(ExtractedDirectory, "audio.source.json"),
                JsonSerializer.Serialize(stamp, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioBootstrap] could not write the stamp: {ex.Message}");
        }
    }
}
