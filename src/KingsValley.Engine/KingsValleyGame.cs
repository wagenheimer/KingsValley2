#nullable enable
using Perf = KingsValley.Engine.Diagnostics.EditorPerformanceLog;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Gum;
using Gum.DataTypes;
using KingsValley.Core.Common;
using KingsValley.Core.SaveSystem;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Navigation;
using KingsValley.Engine.UI.Gum;
using Wagenheimer.MonoGameHelper.Graphics;
using Wagenheimer.MonoGameHelper.Input;
using Wagenheimer.MonoGameHelper.Layout;
using Wagenheimer.MonoGameHelper.Localization;
using Wagenheimer.MonoGameHelper.UI;

namespace KingsValley.Engine;

/// <summary>
/// Main game class.
///
/// Rendering model (see `AGENTS.md`):
/// - Rendering at **native resolution** (`SceneResolutionPolicy.None` in the scene, 1:1 blit).
/// - **Design** coordinate space (1430x768 inherited from Unity) through
///   <see cref="DesignViewport"/> — the bridge is a camera zoom, not a reduced RenderTarget.
/// - UI in **Gum**, always above the scene.
/// - <see cref="PostUiOverlay"/> drawn after Gum: cursor, episodic effects and texts
///   that need to stay above the UI.
/// </summary>
public class KingsValleyGame : Nez.Core
{
    /// <summary>Gum instance used across the project. All UI is created through it.</summary>
    public GumService GumUI => GumService.Default;

    /// <summary>
    /// Gum project loaded (`.gumx`). Needed to **instantiate `.gusx` screens** at runtime
    /// (see <see cref="GumScreens"/>): the project is the "factory" the Gum runtime needs to
    /// materialize a screen's `<Instance>`s. It stays null if the project could not be loaded.
    /// </summary>
    public static GumProjectSave? GumProject { get; private set; }

    /// <summary>Path of the Gum project, relative to the <c>Content</c> folder.</summary>
    private const string GumProjectPath = "Gum/GumProject.gumx";

    /// <summary>
    /// Allows starting the window at another resolution: <c>KINGSVALLEY_WINDOW=1920x1080</c>.
    /// </summary>
    private const string WindowSizeVariable = "KINGSVALLEY_WINDOW";

    private static int InitialWidth => ParseWindowSize().Width;
    private static int InitialHeight => ParseWindowSize().Height;

    /// <summary>Current game instance (the typed <see cref="Nez.Core.Instance"/>).</summary>
    public static KingsValleyGame? Current => Nez.Core.Instance as KingsValleyGame;

    /// <summary>
    /// Diagnostic (screenshot/CI or `KINGSVALLEY_WINDOW`) forces windowed mode — otherwise the fidelity
    /// gate would come out at full-screen resolution.
    /// </summary>
    private static bool DiagnosticsForceWindowed =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KINGSVALLEY_SCREENSHOT")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WindowSizeVariable));

    private ScreenshotRequest? _screenshotRequest;
    private int _screenshotFallback;
    private Point _lastGumPixelSize = new Point(-1, -1);

    private static (int Width, int Height) ParseWindowSize()
    {
        var raw = Environment.GetEnvironmentVariable(WindowSizeVariable);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var parts = raw.Split('x', 'X');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w) && w > 0
                && int.TryParse(parts[1], out var h) && h > 0)
            {
                return (w, h);
            }
        }

        return (GameConstants.DefaultWindowWidth, GameConstants.DefaultWindowHeight);
    }

    private static bool IsBackgroundCapture =>
        Environment.GetEnvironmentVariable("KINGSVALLEY_CAPTURE_BACKGROUND") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KINGSVALLEY_CAPTURE_DIR"));

    public KingsValleyGame() : base(
        width: InitialWidth,
        height: InitialHeight,
        isFullScreen: false,
        windowTitle: GameConstants.GameTitle,
        contentDirectory: "Content")
    {
        Window.AllowUserResizing = true;
        // Explicit unattended capture keeps advancing when another application has focus.
        if (IsBackgroundCapture)
            Nez.Core.PauseOnFocusLost = false;
        Window.ClientSizeChanged += OnClientSizeChanged;

        // MSX2 pixel art (256x212 magnified by the camera zoom): POINT filter, so the pixel
        // stays crisp at any resolution (the bilinear inherited from the base project blurred the art).
        Nez.Core.DefaultSamplerState = SamplerState.PointClamp;

        // Disables Nez's automatic exit on ESC. ESC must never close the application directly.
        ExitOnEscapeKeypress = false;
        Nez.Core.ExitOnEscapeKeypress = false;
    }

    protected override void Initialize()
    {
        base.Initialize();
        Window.Title = GameConstants.GameTitle;
        ExitOnEscapeKeypress = false;
        Nez.Core.ExitOnEscapeKeypress = false;

        if (!IsBackgroundCapture && OperatingSystem.IsWindows() && Window.Handle != IntPtr.Zero)
        {
            try
            {
                ShowWindow(Window.Handle, 9); // SW_RESTORE
                SetForegroundWindow(Window.Handle);
            }
            catch
            {
            }
        }

        // 0. Resolution model: defines the design space and starts following the back buffer.
        DesignViewport.Configure(GameConstants.DesignWidth, GameConstants.DesignHeight);
        DesignViewport.Update(GraphicsDevice);

        // 0.0.1 Graphics variant (MSX2/ MSX1): defines which folder the art is read from.
        Assets.GfxVariant.Initialize();

        // 0.1 Layer above the UI (cursor, episodic effects, floating texts).
        PostUiOverlay.Initialize(GraphicsDevice);

        // 0.1.1 Screen fade (transitions between scenes). Drawn BETWEEN the scene and the UI —
        //       so the loading/HUD stays visible above the curtain.
        ScreenFade.Initialize(GraphicsDevice);

        // 0.2 Optional diagnostic: captures the final screen and exits (fidelity/CI gate).
        _screenshotRequest = ScreenshotRequest.FromEnvironment(
            "KINGSVALLEY_SCREENSHOT", "KINGSVALLEY_SCREENSHOT_FRAME");

        // 1. Initializes Gum (the project's only UI system). The return is the loaded project —
        //    kept to instantiate `.gusx` screens at runtime (`GumScreens`).
        GumProject = GumUI.Initialize(this, GumProjectPath);

        // 1.1 Dynamic fonts (Unity's original TTFs) — they must come after Gum.
        UI.Gum.GumFonts.Initialize(GraphicsDevice);

        // 1.2 Localization (i18n): save (or OS) culture over Content/Localization.
        var save = ProfileSaveService.Load();
        Localization.Initialize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content"), save?.GameLanguage);
        Console.WriteLine($"[Localization] active language: {Localization.Culture}");

        // 1.3 Title art language: the cartridge carries Japanese and English (KINGSVALLEY_TITLE_LANG > save).
        RomContent.UseTitleLanguage(RomContent.ParseTitleLanguage(
            Environment.GetEnvironmentVariable("KINGSVALLEY_TITLE_LANG") ?? save?.TitleLanguage));
        Console.WriteLine($"[RomContent] title: {RomContent.TitleArtLanguage}");

        // 2. Initializes the Multi-Input Helper and the Hybrid Cursor
        InputHelperManager.Initialize(this, GameConstants.DesignWidth, GameConstants.DesignHeight);

        // 2.1 Gum input bridge: swaps the default cursor (bound to the hardware mouse) for one
        //     that reads the project's hybrid pointer (mouse OR the GamePad virtual cursor). From
        //     here the Gum Forms controls (Button/CheckBox/Slider...) respond to the game's input.
        global::Gum.Forms.FormsUtilities.SetCursor(new UI.Gum.InputHelperCursor(InputHelperManager.Instance));

        // 2.2 The Gum Forms controls use OUR components through `GumFormsTheme.CreateButton(...)`
        //     (factory — see the comment in the class; `DefaultFormsTemplates` does not work for
        //     a file component with a behavior).

        // 3. Registers the custom hardware cursor for Mouse mode
        string hardwareCursorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content/cursor_hardware.png");
        if (File.Exists(hardwareCursorPath))
        {
            using var stream = File.OpenRead(hardwareCursorPath);
            var texture = Texture2D.FromStream(GraphicsDevice, stream);
            InputHelperManager.Instance.CursorManager.SetCustomHardwareCursor(texture, 0, 0);
        }

        // 3.1 Applies the save preferences (Full screen / Custom cursor), as Unity does
        //     at boot (`Main.cs:180`). The screenshot gate does not enter full screen.
        var preferencias = ProfileSaveService.Load() ?? new SaveGameData();
        SetCustomCursor(preferencias.CustomCursor);

        if (!DiagnosticsForceWindowed)
            SetFullScreen(preferencias.FullScreen);

        // 3.2 Free version: the audio (and, later, the other assets) comes from the player's ROM. Without a package,
        //     it asks for the ROM (native system dialog) and extracts in the background — the game goes on
        //     and the sound comes in when it finishes.
        string? initialScene=Environment.GetEnvironmentVariable("KINGSVALLEY_START_SCENE")?.ToLowerInvariant();
        if(initialScene is not ("firstrun" or "first_run" or "setup" or "rom"))AudioBootstrap.AutoStart();

        // 3.2.1 Universal plan B: dragging the .rom/.zip onto the window starts the extraction (works on
        //       any system, including Linux without zenity/kdialog).
        Window.FileDrop += (_, e) =>
        {
            if(e.Files.Length>0)
            {
                if(Nez.Core.Scene is Scenes.FirstRunScene setup)setup.SelectCartridge(e.Files[0]);
                else AudioBootstrap.AcceptDrop(e.Files[0]);
            }
        };

        // 4. Initial scene. The normal flow is Splash -> MainMenu -> Loading -> Level.
        //    KINGSVALLEY_START_SCENE allows entering a scene directly (tests/CI):
        //      splash (default) | menu | loading | level
        StartInitialScene();
    }

    /// <summary>Window size before entering full screen (to come back exactly as it was).</summary>
    private (int Width, int Height)? _windowedSize;

    /// <summary>
    /// Turns full screen on/off at once (equivalent to Unity's <c>SetFullScreen</c>).
    ///
    /// <para>
    /// Uses <b>borderless windowed full screen</b> (<see cref="GraphicsDeviceManager.HardwareModeSwitch"/>
    /// = <c>false</c>): the window covers the monitor at the desktop resolution and the <b>Windows video
    /// mode does not change</b>. The <c>true</c> (default) is the classic exclusive, which changes the
    /// system resolution — that is what caused the "blink" and the resolution change that were reported.
    /// </para>
    ///
    /// <para>
    /// The window is resizable and <see cref="DesignViewport"/> follows the new back buffer; when
    /// leaving full screen, the window goes back to the size it had before.
    /// </para>
    /// </summary>
    public void SetFullScreen(bool fullScreen)
    {
        var graphics = Nez.Screen.GraphicsDeviceManager;
        if (graphics == null || graphics.IsFullScreen == fullScreen)
            return;

        // ⚠️ The ORDER matters (MonoGame quirks in "borderless window" mode): when LEAVING fullscreen
        // you must turn `IsFullScreen` off and apply BEFORE touching the size — otherwise the real back
        // buffer stays at the monitor size and the `DesignViewport` (which reads
        // `PresentationParameters`) never comes back, leaving scene and UI at the old zoom.
        graphics.HardwareModeSwitch = false;

        if (fullScreen)
        {
            _windowedSize = (graphics.PreferredBackBufferWidth, graphics.PreferredBackBufferHeight);

            var display = GraphicsDevice.Adapter.CurrentDisplayMode;
            graphics.PreferredBackBufferWidth = display.Width;
            graphics.PreferredBackBufferHeight = display.Height;

            graphics.IsFullScreen = true;
            graphics.ApplyChanges();
        }
        else
        {
            graphics.IsFullScreen = false;
            graphics.ApplyChanges();

            if (_windowedSize.HasValue)
            {
                graphics.PreferredBackBufferWidth = _windowedSize.Value.Width;
                graphics.PreferredBackBufferHeight = _windowedSize.Value.Height;
                _windowedSize = null;
            }

            graphics.ApplyChanges();
        }

        // `DesignViewport` follows the back buffer every frame, but we reapply it here so the
        // first frame after the switch already comes out at the right size.
        DesignViewport.Update(GraphicsDevice);

        // ⚠️ `PresentationParameters` may say 3840x2160 while the **drawing Viewport**
        // stays 1430x768 — the scene then renders only in the top-left corner and the rest stays
        // black. We reassert the viewport at the real back buffer size.
        var parameters = GraphicsDevice.PresentationParameters;
        GraphicsDevice.Viewport = new Viewport(0, 0, parameters.BackBufferWidth, parameters.BackBufferHeight);

        // ⚠️ Nez only recreates the `SceneRenderTarget` (and reapplies the scene camera/resolution) when
        // it receives `CoreEvents.GraphicsDeviceReset`. In "borderless window" mode that event is NOT
        // fired, so the SCENERY kept rendering at the old size (only the top-left
        // corner) even with the right viewport/back buffer. The Gum UI follows through
        // `DesignViewport`, which is why it was the only part that behaved.
        Nez.Core.Emitter.Emit(Nez.CoreEvents.GraphicsDeviceReset);

        UpdateGumViewport();

        // Diagnostic: what matters is the REAL back buffer (PresentationParameters) and the
        // drawing viewport — that is what the scene camera/RT use.
        var viewport = GraphicsDevice.Viewport;
        var bounds = Window.ClientBounds;
        Console.WriteLine(
            $"[KingsValleyGame] Full screen (borderless window): {fullScreen} | " +
            $"window {bounds.Width}x{bounds.Height} | real backbuffer {parameters.BackBufferWidth}x{parameters.BackBufferHeight} | " +
            $"viewport {viewport.Width}x{viewport.Height} | " +
            $"design {DesignViewport.DesignWidth:0.#}x{DesignViewport.DesignHeight:0.#} @ zoom {DesignViewport.Scale:0.###}");
    }

    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        var bounds = Window.ClientBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var graphics = Nez.Screen.GraphicsDeviceManager;
        if (graphics == null)
            return;

        if (graphics.PreferredBackBufferWidth != bounds.Width ||
            graphics.PreferredBackBufferHeight != bounds.Height)
        {
            graphics.PreferredBackBufferWidth = bounds.Width;
            graphics.PreferredBackBufferHeight = bounds.Height;
            graphics.ApplyChanges();

            var parameters = GraphicsDevice.PresentationParameters;
            GraphicsDevice.Viewport = new Viewport(0, 0, parameters.BackBufferWidth, parameters.BackBufferHeight);

            DesignViewport.Update(GraphicsDevice);
            Nez.Core.Emitter.Emit(Nez.CoreEvents.GraphicsDeviceReset);
            UpdateGumViewport();
        }
    }

    /// <summary>
    /// Turns the custom cursor on/off in the package's hybrid component
    /// (<c>HybridCursorManager</c>): with <c>false</c> it goes back to the system's default arrow. In
    /// GamePad mode the virtual cursor stays on top (the toggle does not affect it).
    /// </summary>
    public void SetCustomCursor(bool custom)
    {
        InputHelperManager.Instance.CursorManager.UseCustomHardwareCursor = custom;
        Console.WriteLine($"[KingsValleyGame] Custom cursor: {custom}.");
    }

    /// <summary>
    /// Starts the scene configured by <c>KINGSVALLEY_START_SCENE</c> (default: <c>splash</c>).
    /// With <c>loading</c>, it already runs the preload steps — useful to validate the screen.
    /// </summary>
    private static void StartInitialScene()
    {
        var requested = Environment.GetEnvironmentVariable("KINGSVALLEY_START_SCENE")?.Trim().ToLowerInvariant();

        // Free version without the audio package or playable data: the first screen explains that it needs the ROM and looks for it on
        // disk — all inside the game, without depending on a system dialog.
        if (requested is null &&
            (!Assets.AudioBootstrap.HasPackage || !Assets.RomContent.CanPlayAny) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KINGSVALLEY_ROM")))
        {
            Nez.Core.Scene = new Scenes.FirstRunScene();
            return;
        }

        switch (requested)
        {
            case "firstrun":
            case "first_run":
            case "setup":
            case "rom":
                string? notice = Environment.GetEnvironmentVariable("KINGSVALLEY_FIRSTRUN_NOTICE");
                Nez.Core.Scene = new Scenes.FirstRunScene(notice);
                break;

            case "campaign":
                Nez.Core.Scene=new Scenes.CampaignSelectionScene();
                break;
            case "contest":
                Nez.Core.Scene=new Scenes.CampaignSelectionScene(stages:true);
                break;
            case "menu":
                GameNavigation.GoTo(GameSceneId.MainMenu);
                break;

            case "loading":
                GameNavigation.GoToLoading(GameSceneId.Level, Assets.GameAssets.CreatePreloadSteps());
                break;

            case "level":
                GameNavigation.GoTo(GameSceneId.Level);
                break;

            case "demo":
                int demoStage = 1;
                var lvlStr = Environment.GetEnvironmentVariable("KINGSVALLEY_LEVEL");
                if (!string.IsNullOrWhiteSpace(lvlStr) && int.TryParse(lvlStr, out var parsedStage))
                    demoStage = parsedStage;
                GameNavigation.GoToDemo(demoStage);
                break;

            case "worldmap":
                GameNavigation.GoToWorldMap(Core.Gameplay.GameRun.New(0));
                break;

            case "intro":
                GameNavigation.GoToIntro(Core.Gameplay.GameRun.New(0));
                break;

            case "puzzle":
                GameNavigation.GoTo(GameSceneId.Puzzle);
                break;

            case "editor":
                GameNavigation.GoTo(GameSceneId.Editor);
                break;

            case "gameover":
                GameNavigation.GoToGameOver(Core.Gameplay.GameRun.New(12345, 3, rest: 0, continueEnabled: true));
                break;

            case "soundtest":
                GameNavigation.GoTo(GameSceneId.SoundTest);
                break;

            case "ending":
                // EF10 of the ROM: 1 = MUSIC STAGE, 2 = PUZZLE STAGE (table 0x9D37 of bank 14).
                var pyramid = int.TryParse(System.Environment.GetEnvironmentVariable("KINGSVALLEY_PYRAMID"),
                    out var p) ? p : 1;
                GameNavigation.GoToEnding(Core.Gameplay.GameRun.New(0), pyramid);
                break;

            default:
                GameNavigation.GoTo(GameSceneId.Splash);
                break;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        Perf.Frame(Nez.Core.Scene?.GetType().Name??"none",IsActive);
        using var updateTiming=Perf.Measure("game.update");
        using(Perf.Measure("scene.update")) base.Update(gameTime);

        // System menu (ESC): input read HERE, after base.Update, so the frame already has the
        // new keyboard/gamepad state. In the menus ESC keeps the scene's function.
        if (Nez.Core.Scene is Scenes.GameScene && GameNavigation.StageOverride != null &&
            (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Escape) || Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F5)))
        {
            GameNavigation.StageOverride = null;
            UI.SystemMenu.Close();
            GameNavigation.GoTo(GameSceneId.Editor);
        }
        else UI.SystemMenu.Update();

        // Diagnostics: KINGSVALLEY_SYSMENU=1 opens the system menu at frame 60 (screenshot/CI gate);
        // "demo" also navigates through it, proving the menu logic without a human at the keyboard.
        string? sysMenu = Environment.GetEnvironmentVariable("KINGSVALLEY_SYSMENU");
        if (sysMenu != null)
        {
            if (Time.FrameCount == 60)
                UI.SystemMenu.Open();
            else if (sysMenu == "demo")
            {
                if (Time.FrameCount == 70) UI.SystemMenu.Select(1);
                if (Time.FrameCount == 80) UI.SystemMenu.Select(1);
                if (Time.FrameCount == 90)
                {
                    Console.WriteLine("[demo] cycle at frame 90");
                    UI.SystemMenu.Cycle(1);
                }
            }
            else if (sysMenu == "save")
            {
                if (Time.FrameCount == 65) UI.SystemMenu.Select(1);
                if (Time.FrameCount == 70) UI.SystemMenu.Confirm();
                if (Time.FrameCount == 75) UI.SystemMenu.Confirm();
            }
            else if (sysMenu == "load")
            {
                if (Time.FrameCount == 65) UI.SystemMenu.Select(2);
                if (Time.FrameCount == 70) UI.SystemMenu.Confirm();
            }
        }

        // F11 cycles MSX2 <-> MSX1 graphics at runtime (persists in the save; recreates the scene).
        // (F2 is already the Sound Test/Jukebox of the debug shortcuts.)
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F11))
            Assets.GfxVariant.Toggle();

        // F12: free version — opens the screen that explains and asks for the ROM (without the package there is no sound or art).
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F12))
        {
            Console.WriteLine("[Debug] F12 shortcut -> extraction screen (ROM)");
            Nez.Core.Scene = new Scenes.FirstRunScene();
        }

        float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Follows the current back buffer size (window resize in real time).
        DesignViewport.Update(GraphicsDevice);

        // Updates the input state machine (Mouse, GamePad, Touch).
        // The conversion below takes the mouse position (back buffer pixels) to DESIGN
        // units — the same space as the scene, the UI and the virtual cursor.
        InputHelperManager.Instance.Update(
            gameTime,
            ponto => DesignViewport.ToDesign(ponto));

        // Feeds the MSX audio stream (music/samples rendered from the VGM)
        Assets.GameAudio.Update();

        // Advances the screen fade (covers the scene change).
        ScreenFade.Instance.Update(deltaTime);

        // Keeps the Gum canvas in the same design coordinate space as the scene.
        UpdateGumViewport();

        // Updates the Gum UI (input, hover, pending layout)
        using(Perf.Measure("gum.update")) GumUI.Update(gameTime);

#if DEBUG
        CheckDebugShortcuts();
#endif
    }

#if DEBUG
    private void CheckDebugShortcuts()
    {
        if(!IsActive || Environment.GetEnvironmentVariable("KINGSVALLEY_IGNORE_INPUT")=="1")return;
        // F2: Sound Test / Music Stage Jukebox
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F2))
        {
            Console.WriteLine("[Debug] Atalho F2 -> Sound Test (Jukebox)");
            GameNavigation.GoTo(GameSceneId.SoundTest);
            return;
        }

        // F3: Puzzle Stage (Minigame deslizante 5x5 do final)
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F3))
        {
            Console.WriteLine("[Debug] Atalho F3 -> Puzzle Game");
            GameNavigation.GoTo(GameSceneId.Puzzle);
            return;
        }

        // F4: World Map (world map cutscene and pyramids)
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F4))
        {
            Console.WriteLine("[Debug] Atalho F4 -> World Map");
            GameNavigation.GoToWorldMap(Core.Gameplay.GameRun.New(0));
            return;
        }

        // F6: Ending - Music Stage (Pyramid 1)
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F6))
        {
            Console.WriteLine("[Debug] Atalho F6 -> Ending (Music Stage)");
            GameNavigation.GoToEnding(Core.Gameplay.GameRun.New(99990), pyramid: 1);
            return;
        }

        // F7: Ending - Puzzle Stage (Pyramid 2)
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F7))
        {
            Console.WriteLine("[Debug] Atalho F7 -> Ending (Puzzle Stage)");
            GameNavigation.GoToEnding(Core.Gameplay.GameRun.New(99990), pyramid: 2);
            return;
        }

        // F8: Level Editor
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F8))
        {
            Console.WriteLine("[Debug] Atalho F8 -> Level Editor");
            GameNavigation.GoTo(GameSceneId.Editor);
            return;
        }

        // F9: Game Over
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F9))
        {
            Console.WriteLine("[Debug] Atalho F9 -> Game Over");
            GameNavigation.GoToGameOver(Core.Gameplay.GameRun.New(12345, 1, rest: 0, continueEnabled: true));
            return;
        }

        // F10: Menu Principal
        if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.F10))
        {
            Console.WriteLine("[Debug] Atalho F10 -> Menu Principal");
            GameNavigation.GoTo(GameSceneId.MainMenu);
            return;
        }
    }
#endif

    protected override void Draw(GameTime gameTime)
    {
        // Nez renders the scene (and blits it to the back buffer) inside base.Draw.
        using var drawTiming=Perf.Measure("game.draw");
        using(Perf.Measure("nez.draw")) base.Draw(gameTime);

        // Transition curtain: covers the WORLD, but stays BELOW the UI (that is why the loading
        // stays visible through it) and BELOW PostUiOverlay (the cursor stays in view).
        ScreenFade.Instance.Draw();

        // Gum draws ALWAYS afterwards: that is what guarantees the UI on top of the scene.
        using(Perf.Measure("gum.draw")) GumUI.Draw();
        Perf.DrawCompleted();

        // After the UI: virtual cursor, episodic effects and floating texts.
        PostUiOverlay.Draw();

        // Frame sequence (docs/GIFs): KINGSVALLEY_CAPTURE_DIR + _FROM/_TO.
        CaptureSequence();

        // The fidelity gate only starts counting frames when the scene is really ready and the
        // transition fade has finished — otherwise, with the window unfocused (very high framerate, tiny
        // dt), the requested frame arrives before the fade opens and the PNG comes out black.
        if (_screenshotRequest is { IsCompleted: false })
        {
            bool ready = Nez.Core.Scene != null
                && !ScreenFade.Instance.IsFading
                && ScreenFade.Instance.Alpha <= 0.01f;

            if (ready || ++_screenshotFallback > ScreenshotFallbackFrames)
            {
                if (_screenshotRequest.TryCapture(GraphicsDevice))
                    Exit();
            }
        }
    }

    /// <summary>Frame limit spent waiting for the scene/fade before capturing anyway (does not hang CI).</summary>
    private const int ScreenshotFallbackFrames = 5000;

    // ── Frame sequence for documentation (GIFs) ───────────────────────────────────────────

    private bool _captureReady;
    private string? _captureDir;
    private int _captureFrom, _captureTo = int.MaxValue, _captureFrame;

    /// <summary>
    /// Writes `frame_NNNN.png` of the back buffer (with Gum and PostUiOverlay) between frames
    /// `KINGSVALLEY_CAPTURE_FROM` and `KINGSVALLEY_CAPTURE_TO` (counted only after the scene opens the
    /// fade) and exits at the end — it is what feeds the GIFs in `docs/en/media`.
    /// </summary>
    private void CaptureSequence()
    {
        if (!_captureReady)
        {
            _captureReady = true;
            _captureDir = Environment.GetEnvironmentVariable("KINGSVALLEY_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(_captureDir))
            {
                int.TryParse(Environment.GetEnvironmentVariable("KINGSVALLEY_CAPTURE_FROM"), out _captureFrom);
                if (!int.TryParse(Environment.GetEnvironmentVariable("KINGSVALLEY_CAPTURE_TO"), out _captureTo))
                    _captureTo = int.MaxValue;

                System.IO.Directory.CreateDirectory(_captureDir);
            }
        }

        if (string.IsNullOrWhiteSpace(_captureDir))
            return;

        bool ready = Nez.Core.Scene != null && !ScreenFade.Instance.IsFading && ScreenFade.Instance.Alpha <= 0.01f;
        if (!ready)
            return;

        if (_captureFrame >= _captureFrom && _captureFrame <= _captureTo)
        {
            var path = System.IO.Path.Combine(_captureDir, $"frame_{_captureFrame:D4}.png");
            BackBufferScreenshot.SaveToFile(GraphicsDevice, path);
        }

        _captureFrame++;
        if (_captureFrame > _captureTo)
            Exit();
    }

    /// <summary>
    /// Synchronizes Gum with the active surface. Gameplay uses the design viewport;
    /// the desktop editor uses physical pixels and explicitly sized text with camera zoom one.
    /// This prevents high-DPI editor labels from being rasterized a second time during Draw.
    /// Canvas changes are applied only when the surface or back buffer changes.
    /// </summary>
    private bool _lastGumEditorWorkspace;

    private void UpdateGumViewport()
    {
        if (!GumUI.IsInitialized)
            return;

        var pixelSize = new Point(DesignViewport.PixelWidth, DesignViewport.PixelHeight);
        bool editorWorkspace = Nez.Core.Scene is Scenes.EditorScene;
        if (pixelSize == _lastGumPixelSize && editorWorkspace == _lastGumEditorWorkspace)
            return;

        _lastGumPixelSize = pixelSize;
        _lastGumEditorWorkspace = editorWorkspace;

        // The editor supplies physical UI coordinates and font sizes. Keeping camera zoom
        // at one avoids oversampling every newly created label again after maximization.
        GumUI.Renderer.Camera.Zoom = editorWorkspace ? 1 : DesignViewport.Scale;
        GumUI.CanvasWidth = editorWorkspace ? pixelSize.X : DesignViewport.DesignWidth;
        GumUI.CanvasHeight = editorWorkspace ? pixelSize.Y : DesignViewport.DesignHeight;

        // The zoom changed: the font materials (Unity gradient) have their atlas rasterized at
        // the size with zoom, so they must be redone for the new scale — otherwise the oversampling
        // regenerates the regular font and the material disappears.
        GumFontMaterials.ReapplyAll();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
