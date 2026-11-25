#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;

// The project is a `WinExe`: opening the .exe directly does NOT open a console, so every `Console.WriteLine` of
// the game (video diagnostics, full screen, HUD, etc.) would be lost. We mirror the output to a file
// next to the executable — running through `dotnet run` it still shows in the terminal too.
TextWriter? logFile = null;
try
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "kingsvalley2.log");
    var stream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
    logFile = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
    Console.SetOut(new TeeWriter(Console.Out, logFile));
    Console.WriteLine($"[Program] Log of this run: {logPath}");
}
catch
{
    // Another instance has the log open; keep writing to the standard stdout
}

Console.WriteLine($"[Program] Start: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

try
{
    using var game = new KingsValley.Engine.KingsValleyGame();

    // SDL sets the window icon from Icon.bmp and macOS propagates it to the Dock. Re-apply the
    // bundle's transparent AppIcon.icns after the window exists so the Dock tile keeps its alpha.
    // The component is a no-op outside macOS and fails silently if AppKit is unavailable.
    if (OperatingSystem.IsMacOS())
        game.Components.Add(new MacDockIconComponent(game));

    game.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"[Program] FATAL EXCEPTION: {ex}");
    logFile?.Flush();
    throw;
}
finally
{
    if(!KingsValley.Engine.Assets.AudioBootstrap.CancelAndWait(TimeSpan.FromSeconds(15)))
        Console.WriteLine("[Program] Extraction shutdown reached its time limit; incomplete staging was not published.");
}

Console.WriteLine($"[Program] Finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
logFile?.Flush();

/// <summary>Writes to two destinations at once: the console (when it exists) and the log file.</summary>
internal sealed class TeeWriter : TextWriter
{
    private readonly TextWriter _console;
    private readonly TextWriter _file;

    public TeeWriter(TextWriter console, TextWriter file)
    {
        _console = console;
        _file = file;
    }

    public override Encoding Encoding => _console.Encoding;

    public override void Write(char value)
    {
        _console.Write(value);
        _file.Write(value);
    }

    public override void Write(string? value)
    {
        _console.Write(value);
        _file.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _console.WriteLine(value);
        _file.WriteLine(value);
    }
}

/// <summary>
/// Applies the bundle icon once the window exists. SDL installs Icon.bmp as the window icon
/// during window creation; on macOS that image also becomes the Dock icon. Running after
/// initialization (and once more on the first update) keeps the transparent AppIcon.icns.
/// </summary>
internal sealed class MacDockIconComponent : GameComponent
{
    private bool _applied;

    public MacDockIconComponent(Game game) : base(game) { }

    public override void Initialize()
    {
        base.Initialize();
        ApplyOnce();
    }

    public override void Update(GameTime gameTime)
    {
        if (!_applied)
            ApplyOnce();
        base.Update(gameTime);
    }

    private void ApplyOnce()
    {
        if (!MacDockIcon.TryUseBundleIcon())
            return;

        _applied = true;
        Enabled = false;
    }
}

/// <summary>
/// Optional AppKit bridge via the Objective-C runtime. Uses only system libobjc (no native
/// dependencies or new entitlements) and treats every failure as "leave the icon as-is".
/// </summary>
internal static class MacDockIcon
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetClass(string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr RegisterSelector(string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SendMessage0(IntPtr receiver, IntPtr selector);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SendMessage1(IntPtr receiver, IntPtr selector, IntPtr argument);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SendVoidMessage1(IntPtr receiver, IntPtr selector, IntPtr argument);

    public static bool TryUseBundleIcon()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        try
        {
            IntPtr libobjc;
            if (!NativeLibrary.TryLoad("/usr/lib/libobjc.A.dylib", out libobjc) &&
                !NativeLibrary.TryLoad("libobjc.A.dylib", out libobjc))
                return false;

            if (!NativeLibrary.TryGetExport(libobjc, "objc_getClass", out var getClassPtr) ||
                !NativeLibrary.TryGetExport(libobjc, "sel_registerName", out var selectorPtr) ||
                !NativeLibrary.TryGetExport(libobjc, "objc_msgSend", out var messagePtr))
                return false;

            var getClass = Marshal.GetDelegateForFunctionPointer<GetClass>(getClassPtr);
            var selector = Marshal.GetDelegateForFunctionPointer<RegisterSelector>(selectorPtr);
            var send0 = Marshal.GetDelegateForFunctionPointer<SendMessage0>(messagePtr);
            var send1 = Marshal.GetDelegateForFunctionPointer<SendMessage1>(messagePtr);
            var sendVoid1 = Marshal.GetDelegateForFunctionPointer<SendVoidMessage1>(messagePtr);

            var nsApplication = getClass("NSApplication");
            var nsImage = getClass("NSImage");
            var nsString = getClass("NSString");
            if (nsApplication == IntPtr.Zero || nsImage == IntPtr.Zero || nsString == IntPtr.Zero)
                return false;

            var app = send0(nsApplication, selector("sharedApplication"));
            if (app == IntPtr.Zero)
                return false;

            // AppIcon.icns is installed in Contents/Resources by tools/macos/package_app.sh.
            var image = ImageNamed(nsImage, nsString, selector, send1, "AppIcon");
            if (image == IntPtr.Zero)
                image = ImageNamed(nsImage, nsString, selector, send1, "AppIcon.icns");
            if (image == IntPtr.Zero)
                return false;

            sendVoid1(app, selector("setApplicationIconImage:"), image);
            return true;
        }
        catch
        {
            // A restricted or partial Objective-C runtime must never stop the game.
            return false;
        }
    }

    private static IntPtr ImageNamed(IntPtr nsImage, IntPtr nsString, RegisterSelector selector,
        SendMessage1 send1, string name)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            // stringWithUTF8String: returns an autoreleased NSString; no manual release needed.
            var nsName = send1(nsString, selector("stringWithUTF8String:"), utf8);
            return nsName == IntPtr.Zero ? IntPtr.Zero : send1(nsImage, selector("imageNamed:"), nsName);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }
}
