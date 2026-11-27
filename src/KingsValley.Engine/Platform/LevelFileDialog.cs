#nullable enable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KingsValley.Engine.Platform;

/// <summary>Native open/save dialogs for portable levels. No command text is evaluated by a shell.</summary>
internal static class LevelFileDialog
{
    public static string? Pick(bool save, string suggestedName = "Untitled.kv2level")
    {
        using var timing=KingsValley.Engine.Diagnostics.EditorPerformanceLog.Measure("dialog.wait_for_user",save?"save":"open");
        if (OperatingSystem.IsWindows())
        {
            using var buffer = new WindowsFileBuffer(save ? Path.GetFileName(suggestedName) : "");
            var data = new OpenFileName { Size = Marshal.SizeOf<OpenFileName>(), Owner = GetActiveWindow(),
                Filter = "King's Valley level (*.kv2level;*.json)\0*.kv2level;*.json\0All files\0*.*\0\0",
                File = buffer.Pointer, MaximumFile = WindowsFileBuffer.Capacity,
                Title = save ? "Save Level As" : "Open Level", DefaultExtension = "kv2level",
                Flags = 0x80000 | 0x800 | 8 | (save ? 2 : 0x1000) };
            if (save ? GetSaveFileNameW(ref data) : GetOpenFileNameW(ref data)) return buffer.Read();
            int error = CommDlgExtendedError();
            if (error != 0) throw new IOException($"The level file dialog failed (Windows error 0x{error:X}).");
            return null;
        }
        if (OperatingSystem.IsMacOS())
        {
            string safe = Path.GetFileName(suggestedName).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return Run("osascript", "-e", save
                ? $"POSIX path of (choose file name with prompt \"Save Level As\" default name \"{safe}\")"
                : "POSIX path of (choose file with prompt \"Open Level\")");
        }
        if (OnPath("zenity")) return save
            ? Run("zenity", "--file-selection", "--save", "--confirm-overwrite", "--title=Save Level As", "--filename=" + Path.GetFileName(suggestedName))
            : Run("zenity", "--file-selection", "--title=Open Level", "--file-filter=Level files | *.kv2level *.json");
        if (OnPath("kdialog")) return Run("kdialog", save ? "--getsavefilename" : "--getopenfilename",
            save ? Path.GetFileName(suggestedName) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "*.kv2level *.json|Level files");
        throw new InvalidOperationException("A file dialog is unavailable. Install zenity or kdialog to open and save external levels.");
    }

    private static bool OnPath(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(p => File.Exists(Path.Combine(p, name)));
    private static string? Run(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("The file dialog could not start.");
        string output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OpenFileName
    {
        public int Size; public IntPtr Owner, Instance; public string? Filter, CustomFilter; public int MaximumCustomFilter, FilterIndex;
        public IntPtr File; public int MaximumFile; public string? FileTitle; public int MaximumFileTitle;
        public string? InitialDirectory, Title; public int Flags; public short FileOffset, ExtensionOffset;
        public string? DefaultExtension; public IntPtr CustomData, Hook; public string? Template;
        public IntPtr Reserved; public int ReservedValue, FlagsEx;
    }

    // OPENFILENAMEW contains a writable WCHAR pointer. StringBuilder is supported
    // as a P/Invoke parameter, but cannot be marshalled as a structure field.
    internal sealed class WindowsFileBuffer : IDisposable
    {
        internal const int Capacity = 4096;
        internal IntPtr Pointer { get; private set; }
        internal WindowsFileBuffer(string initial)
        {
            if (initial.Length >= Capacity) throw new ArgumentException("The suggested level file name is too long.", nameof(initial));
            var characters = new char[Capacity]; initial.CopyTo(0, characters, 0, initial.Length);
            Pointer = Marshal.AllocHGlobal(Capacity * sizeof(char));
            try { Marshal.Copy(characters, 0, Pointer, Capacity); }
            catch { Dispose(); throw; }
        }
        internal string Read() => Marshal.PtrToStringUni(Pointer) ?? "";
        public void Dispose()
        {
            if (Pointer == IntPtr.Zero) return;
            Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero;
        }
    }
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OpenFileName data);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileNameW(ref OpenFileName data);
    [DllImport("comdlg32.dll")] private static extern int CommDlgExtendedError();
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
}
