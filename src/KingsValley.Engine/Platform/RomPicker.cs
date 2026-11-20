#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace KingsValley.Engine.Platform;

/// <summary>
/// Native "open file" dialog for the cartridge, on every desktop the game runs on.
/// </summary>
/// <remarks>
/// <para>Used once, on the first run of the free build, to ask for the player's ROM. The dialog is
/// modal and runs on the calling thread, which is fine because it happens at boot, before the first
/// scene.</para>
///
/// <para><b>Why not SDL:</b> <c>SDL_ShowOpenFileDialog</c> only exists from SDL 2.0.18 and the SDL2 that
/// MonoGame ships is older — the call failed silently and the game booted without asking anything.</para>
///
/// <para><b>Per system:</b> Windows uses the common dialog (<c>GetOpenFileNameW</c>); macOS uses
/// <c>osascript</c> (always present); Linux uses <c>zenity</c> or <c>kdialog</c> (present on GNOME and
/// KDE desktops). <see cref="IsAvailable"/> tells the caller whether a dialog can be shown, so the
/// game can fall back to the environment variable / file drop.</para>
/// </remarks>
internal static class RomPicker
{
    private const string Title = "Select your King's Valley II cartridge (.rom or .zip)";

    /// <summary>Can a dialog be shown here?</summary>
    public static bool IsAvailable
    {
        get
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                return true;

            if (OperatingSystem.IsLinux())
                return OnPath("zenity") || OnPath("kdialog");

            return false;
        }
    }

    /// <summary>Shows the dialog (blocking) and returns the chosen path, or <c>null</c>.</summary>
    public static string? Pick(string title = Title)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return WindowsDialog.Show(title);

            if (OperatingSystem.IsMacOS())
                return MacDialog.Show(title);

            if (OperatingSystem.IsLinux())
                return LinuxDialog.Show(title);

            Console.WriteLine("[RomPicker] no dialog on this OS — use KINGSVALLEY_ROM=<path>.");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RomPicker] dialog failed: {ex.Message}");
            return null;
        }
    }

    // ── Windows ──────────────────────────────────────────────────────────────────

    private static class WindowsDialog
    {
        private const int OpenFileMustExist = 0x00001000;
        private const int OpenFilePathMustExist = 0x00000800;
        private const int OpenFileNoChangeDir = 0x00000008;
        private const int OpenFileExplorer = 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int StructSize;
            public IntPtr Owner;
            public IntPtr Instance;
            public string? Filter;
            public string? CustomFilter;
            public int MaxCustomFilter;
            public int FilterIndex;
            public string? File;
            public int MaxFile;
            public string? FileTitle;
            public int MaxFileTitle;
            public string? InitialDirectory;
            public string? Title;
            public int Flags;
            public short FileOffset;
            public short FileExtension;
            public string? DefExt;
            public IntPtr CustomData;
            public IntPtr Hook;
            public string? TemplateName;
            public IntPtr Reserved0;
            public int Reserved1;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName dialog);

        public static string? Show(string title)
        {
            // "description\0patterns\0" pairs, ended by an extra \0.
            const string filter =
                "MSX cartridge (*.rom, *.zip)\0*.rom;*.zip;*.ROM;*.ZIP\0All files (*.*)\0*.*\0\0";

            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Filter = filter,
                File = new string('\0', 4096),
                MaxFile = 4096,
                Title = title,
                Flags = OpenFileMustExist | OpenFilePathMustExist | OpenFileNoChangeDir | OpenFileExplorer,
            };

            if (!GetOpenFileNameW(ref dialog))
                return null;

            return string.IsNullOrWhiteSpace(dialog.File) ? null : dialog.File;
        }
    }

    // ── macOS ────────────────────────────────────────────────────────────────────

    private static class MacDialog
    {
        public static string? Show(string title)
        {
            // `choose file` blocks until the user picks or cancels (exit code 1 on cancel).
            string script =
                $"POSIX path of (choose file with prompt \"{Escape(title)}\")";
            string? result = Run("osascript", $"-e \"{Escape(script)}\"");
            return CleanPath(result);
        }
    }

    // ── Linux ────────────────────────────────────────────────────────────────────

    private static class LinuxDialog
    {
        public static string? Show(string title)
        {
            if (OnPath("zenity"))
            {
                string? result = Run("zenity",
                    $"--file-selection --title=\"{Escape(title)}\" " +
                    "--file-filter=\"MSX cartridge | *.rom *.zip *.ROM *.ZIP\" --file-filter=\"All files | *\"");
                return CleanPath(result);
            }

            if (OnPath("kdialog"))
            {
                string? result = Run("kdialog",
                    $"--getopenfilename \"$HOME\" \"*.rom *.zip *.ROM *.ZIP|MSX cartridge\" --title \"{Escape(title)}\"");
                return CleanPath(result);
            }

            Console.WriteLine("[RomPicker] install zenity or kdialog to get a file dialog on Linux.");
            return null;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static string? Run(string file, string arguments)
    {
        var info = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process? process = Process.Start(info);
        if (process is null)
            return null;

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0 ? output.Trim() : null;
    }

    private static string? CleanPath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string path = text.Trim().Trim('"');
        return File.Exists(path) ? path : null;
    }

    private static bool OnPath(string executable)
    {
        try
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (string folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(folder, executable)))
                    return true;
            }
        }
        catch (Exception)
        {
            // fall through: treat as unavailable
        }

        return false;
    }

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
