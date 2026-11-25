#nullable enable
using System;
using System.Runtime.InteropServices;

namespace KingsValley.Engine.Platform;

/// <summary>
/// One-button native message box through SDL2 — the universal fallback when no file dialog exists.
/// </summary>
/// <remarks>
/// <c>SDL_ShowSimpleMessageBox</c> is present in every SDL2 build (MonoGame ships one), so this works
/// on Windows, Linux and macOS even when <see cref="RomPicker"/> cannot show a picker. The text is what
/// tells the player what to do (drop the cartridge on the window, or set the environment variable).
/// </remarks>
internal static class SdlMessageBox
{
    private const int SdlMessageBoxInformation = 1;
    private const int SdlMessageBoxWarning = 2;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ShowSimpleMessageBox(int flags, IntPtr title, IntPtr message, IntPtr window);

    private static ShowSimpleMessageBox? _show;
    private static bool _tried;

    /// <summary>Shows the message (returns false when SDL cannot provide it).</summary>
    public static bool Show(string title, string message, bool warning = false)
    {
        if (!EnsureLoaded() || _show is null)
            return false;

        IntPtr titlePtr = Utf8(title);
        IntPtr messagePtr = Utf8(message);

        try
        {
            return _show(warning ? SdlMessageBoxWarning : SdlMessageBoxInformation,
                titlePtr, messagePtr, IntPtr.Zero) == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SdlMessageBox] failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (titlePtr != IntPtr.Zero) Marshal.FreeHGlobal(titlePtr);
            if (messagePtr != IntPtr.Zero) Marshal.FreeHGlobal(messagePtr);
        }
    }

    private static bool EnsureLoaded()
    {
        if (_tried)
            return _show is not null;

        _tried = true;

        foreach (string name in new[] { "SDL2", "SDL2.dll", "libSDL2-2.0.so.0", "libSDL2-2.0.0.dylib" })
        {
            if (!NativeLibrary.TryLoad(name, out IntPtr handle))
                continue;

            if (!NativeLibrary.TryGetExport(handle, "SDL_ShowSimpleMessageBox", out IntPtr export))
                continue;

            _show = Marshal.GetDelegateForFunctionPointer<ShowSimpleMessageBox>(export);
            return true;
        }

        return false;
    }

    private static IntPtr Utf8(string text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }
}
