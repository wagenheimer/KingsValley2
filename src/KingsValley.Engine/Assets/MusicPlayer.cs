#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Engine.Assets;

/// <summary>
/// Opens the extracted music in **MSXScope** (the player of the <c>Wagenheimer.MsxAudio</c> package), installing it
/// on demand when it does not exist yet — nothing ships with the game.
///
/// <para>Search order: <c>KINGSVALLEY_PLAYER</c> (diagnostic) → next to the game → a previous
/// installation (<c>%APPDATA%\MSXScope\&lt;tag&gt;</c>). If it is not found, downloads the release from the repository
/// (with progress) and opens the <c>audio.msxaudio</c> package.</para>
/// </summary>
public static class MusicPlayer
{
    private static Task<string?>? _task;
    private static CancellationTokenSource? _cancel;
    private static double _progress;

    /// <summary>Is it installing/downloading now?</summary>
    public static bool IsRunning => _task is { IsCompleted: false };

    /// <summary>Short status text (the PLAYER line of the system menu).</summary>
    public static string Status
    {
        get
        {
            lock (Gate)
            {
                if (IsRunning)
                    return _progress > 0 ? $"{_progress * 100:0}%" : "GETTING";

                return Exists ? "OPEN" : "GET";
            }
        }
    }

    /// <summary>Is there already a usable player?</summary>
    public static bool Exists => FindPlayer() != null;

    private static readonly object Gate = new();

    /// <summary>
    /// Opens the package in the player. Returns <c>false</c> when there is no audio package (nothing to play).
    /// </summary>
    /// <param name="packagePath">The extracted <c>audio.msxaudio</c>.</param>
    public static bool Open(string? packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            Console.WriteLine("[MusicPlayer] no audio package");
            return false;
        }

        string? installed = FindPlayer();
        if (installed != null)
        {
            Launch(installed, packagePath);
            return true;
        }

        if (!MsxScopeInstaller.IsSupported)
        {
            Console.WriteLine("[MusicPlayer] the player is only available on Windows");
            return false;
        }

        if (IsRunning)
            return true;

        lock (Gate)
        {
            _progress = 0;
        }

        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;

        _task = Task.Run(async () =>
        {
            try
            {
                MsxScopeRelease release = await MsxScopeInstaller.GetLatestAsync(token).ConfigureAwait(false);

                var progress = new Progress<(long Done, long Total)>(p =>
                {
                    lock (Gate)
                        _progress = p.Total > 0 ? (double)p.Done / p.Total : 0;
                });

                string exe = await MsxScopeInstaller.InstallAsync(release, progress, token).ConfigureAwait(false);
                Console.WriteLine($"[MusicPlayer] installed {release.Tag} -> {exe}");

                Launch(exe, packagePath);
                return exe;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[MusicPlayer] install cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MusicPlayer] install failed: {ex.Message}");
                return null;
            }
            finally
            {
                lock (Gate)
                {
                    _progress = 0;
                }

                _task = null;
            }
        });

        return true;
    }

    /// <summary>Cancela um download em andamento.</summary>
    public static void Cancel()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
    }

    private static void Launch(string exe, string packagePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, $"\"{packagePath}\"") { UseShellExecute = true });
            Console.WriteLine($"[MusicPlayer] {Path.GetFileName(exe)} <- {Path.GetFileName(packagePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MusicPlayer] could not start: {ex.Message}");
        }
    }

    /// <summary>Looks for the player: environment → next to the game → previous installation.</summary>
    private static string? FindPlayer()
    {
        string? configured = Environment.GetEnvironmentVariable("KINGSVALLEY_PLAYER");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured))
                return configured;

            string inDir = Path.Combine(configured, MsxScopeInstaller.ExecutableName);
            if (File.Exists(inDir))
                return inDir;
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] local =
        {
            Path.Combine(baseDir, MsxScopeInstaller.ExecutableName),
            Path.Combine(baseDir, "player", MsxScopeInstaller.ExecutableName),
        };

        foreach (string candidate in local)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return MsxScopeInstaller.Locate();
    }
}
