#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.SaveSystem;
using KingsValley.Core.Stages;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Input;
using KingsValley.Engine.Navigation;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// The game: runs the Core <see cref="StageSession"/> at <b>30 Hz</b> (like the original, 1 tick every
/// 2 frames), plays the event sounds and takes care of the transitions (stage intro, death, game over,
/// stage cleared). The drawing lives in <see cref="RoomRenderer"/>.
///
/// Controls (the MSX ones): arrows = walk/ladder · Space/Z = jump or use the item ·
/// F1 = pause · F5 = suicide (restarts the stage losing a life) · Esc = menu.
/// </summary>
public class GameScene : SceneBase
{
    private const float TickSeconds = GameConstants.FramesPerTick / (float)GameConstants.FramesPerSecond;
    private const int IntroTicks = 60;
    private const int GameOverTicks = 120;

    /// <summary>
    /// 7342: E257 zeroes after the stage-end screen (text, password and the wait for the key) and the
    /// next stage loads — one tick just for the scene to change.
    /// </summary>
    private const int ClearedTicks = 1;

    /// <summary>
    /// After the death animation the original still holds the screen (E246=0 → 4934: E204=0x20 and changes
    /// mode) until Vick reappears: 222 ticks in state 5 against 100 for the animation. Measured in the
    /// emulator (stage 3 trace, <c>tools/kv2-extract/emulator/trace_enemy.tcl</c>).
    /// </summary>
    private const int DeathHoldTicks = 222 - 100;

    private enum Phase { NoData, Intro, Playing, Cleared, GameOver, Paused, DeathHold }

    private readonly GameRun? _requested;
    private RoomBackgroundBuilder? _roomBackgrounds;
    private GameRun _run = GameRun.New(0);
    private StageSession? _session;

    /// <summary>State of the run in progress (used by the graphics toggle when recreating the scene).</summary>
    public GameRun Run => _run;

    /// <summary>Restarts the current stage (suicide losing 1 life, identical to MSX F5).</summary>
    public void RestartStage()
    {
        if (_phase is Phase.Playing or Phase.Intro or Phase.Paused && _session != null)
        {
            if (_phase == Phase.Paused)
                _phase = Phase.Playing;
            _session.Suicide();
        }
    }

    /// <summary>Captures complete SaveState data from the current gameplay session.</summary>
    public SaveStateData? CreateSaveState(int slot)
    {
        if (_session == null) return null;

        return new SaveStateData
        {
            Slot = slot,
            Timestamp = DateTime.Now,
            Stage = _run.Stage,
            Pyramid = _session.Definition.Pyramid,
            Room = _session.CurrentRoom,
            Score = _run.Score + _session.Score,
            Lives = _run.Rest,
            SoulStonesRemaining = _session.DoorStuds,
            TotalSoulStones = _session.SoulStoneTotal,
            GfxRom = GfxVariant.Current.Name,
            Run = new GameRunData
            {
                Stage = _run.Stage,
                Lives = _run.Rest,
                HiScore = _run.HiScore,
                StageScore = _run.Score,
                Invincible = _run.Invincible,
                ContinueEnabled = _run.ContinueEnabled,
            },
            Session = _session.CaptureState(),
        };
    }

    /// <summary>Restores a complete gameplay session from a SaveState.</summary>
    public bool LoadSaveState(SaveStateData data)
    {
        try
        {
            var def = GameNavigation.StageOverride ?? StageLoader.Load(RomContent.LevelsDirectory, data.Run.Stage);
            _session = new StageSession(def) { Invincible = data.Run.Invincible };
            _session.RestoreFrom(data.Session);

            _run = new GameRun(
                data.Run.Stage,
                data.Run.Lives,
                data.Run.StageScore,
                data.Run.HiScore,
                data.Run.Invincible,
                data.Run.ContinueEnabled);

            _phase = Phase.Playing;
            _phaseTicks = 0;
            _actionLatched = false;
            _navigator.Cancel();

            _roomBackgrounds?.Invalidate();
            GameAudio.PlayStage(def);

            Console.WriteLine($"[GameScene] Restored SaveState slot {data.Slot}: Stage {_run.Stage}, Room {_session.CurrentRoom}, Score {_run.Score + _session.Score}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameScene] Failed to load SaveState: {ex.Message}");
            return false;
        }
    }
    private Phase _phase = Phase.Intro;
    private float _accumulator;
    private int _phaseTicks;
    private bool _actionLatched;
    private int _doorFlashTicks;
    private bool _exitDiagnostic;
    private bool _exitHold;
    private readonly PointAndClickNavigator _navigator = new();

    private readonly bool _isDemo;

    /// <summary>Run from the environment (<c>KINGSVALLEY_LEVEL</c> / <c>KINGSVALLEY_PASSWORD</c>) or from stage 1.</summary>
    public GameScene() : this(null, false)
    {
    }

    /// <summary>Explicit run (menu: NORMAL or password) or Attract/Demo mode.</summary>
    public GameScene(GameRun? run, bool isDemo = false)
    {
        _requested = run;
        _isDemo = isDemo;
    }

    protected override void OnInitialize()
    {
        AddEntity(new Entity("room")).AddComponent(new RoomRenderer(
            () => _session,
            () => _run,
            s => _roomBackgrounds?.Update(s!),
            Banner,
            () => _navigator.TargetPixel,
            () => _doorFlashTicks));

        if (!RomContent.IsAvailable)
        {
            _phase = Phase.NoData;
            Console.WriteLine($"[GameScene] ROM data missing at {RomContent.Root} — run tools/kv2-extract.");
            return;
        }

        _roomBackgrounds = new RoomBackgroundBuilder(Nez.Core.GraphicsDevice);
        GameAudio.Initialize();
        _run = _requested ?? RunFromEnvironment();
        StartStage();
    }

    private static GameRun RunFromEnvironment()
    {
        int hi = ProfileSaveService.Load().HiScore;
        if (GameNavigation.StageOverride != null)
            return GameRun.New(hi, GameNavigation.StageOverride.Number);

        var password = Environment.GetEnvironmentVariable("KINGSVALLEY_PASSWORD")?.Trim();
        if (string.Equals(password, StagePassword.Festival, StringComparison.OrdinalIgnoreCase))
            return GameRun.Cheat(hi, invincible: true);          // 6B24: stage 1, REST 1, E255 = 1
        if (string.Equals(password, StagePassword.TryAgain, StringComparison.OrdinalIgnoreCase))
            return GameRun.Cheat(hi, continueEnabled: true);     // 6B24: stage 1, REST 1, E217 = 1
        if (password != null && StagePassword.TryDecode(password, out int ps, out int pl))
            return GameRun.New(hi, ps, pl);

        var raw = Environment.GetEnvironmentVariable("KINGSVALLEY_LEVEL");
        return int.TryParse(raw, out int stage) && stage > 0 ? GameRun.New(hi, stage) : GameRun.New(hi);
    }

    private void StartStage()
    {
        var def = GameNavigation.StageOverride ?? StageLoader.Load(RomContent.LevelsDirectory, _run.Stage);
        _session = new StageSession(def) { Invincible = _run.Invincible };
        bool skipIntro = Environment.GetEnvironmentVariable("KINGSVALLEY_SKIP_LEVELSTART") == "1";
        _phase = skipIntro ? Phase.Playing : Phase.Intro;
        _phaseTicks = 0;
        GameAudio.PlayStage(def);
        Console.WriteLine($"[GameScene] stage {_run.Stage} (pyramid {def.Pyramid}) — password {_run.Password}");

        // Diagnostic (KINGSVALLEY_EXIT=N): enters through the door and advances N ticks of the exit sequence
        // (71E6). It stays deterministic — it does not depend on the window framerate, unlike hunting the
        // frame in `KINGSVALLEY_SCREENSHOT_FRAME`. It only holds on the 1st stage (otherwise the stage would change forever).
        ApplyExitDiagnostic();

        // Diagnostic (KINGSVALLEY_PLAYER=room,x,y[,item[,facing]]): positions Vick (documentation scripts).
        var place = Environment.GetEnvironmentVariable("KINGSVALLEY_PLAYER");
        if (!string.IsNullOrWhiteSpace(place))
        {
            var p = place.Split(',');
            if (p.Length >= 3 && int.TryParse(p[0], out int room) && int.TryParse(p[1], out int px)
                && int.TryParse(p[2], out int py))
            {
                _session!.PlacePlayer(room, px, py);
                if (p.Length >= 4 && Enum.TryParse<ItemKind>(p[3], true, out var item))
                    _session.Player.Holding = item;
                if (p.Length >= 5 && Enum.TryParse<Facing>(p[4], true, out var facing))
                    _session.Player.Facing = facing;
                Console.WriteLine($"[GameScene] KINGSVALLEY_PLAYER -> room {room} ({px},{py}), holding {_session.Player.Holding}, facing {_session.Player.Facing}");
            }
        }

        var itemEnv = Environment.GetEnvironmentVariable("KINGSVALLEY_ITEM");
        if (!string.IsNullOrWhiteSpace(itemEnv))
        {
            if (Enum.TryParse<ItemKind>(itemEnv, true, out var itm))
                _session!.Player.Holding = itm;
            else if (int.TryParse(itemEnv, out int itmId))
                _session!.Player.Holding = (ItemKind)itmId;
        }
    }

    /// <summary>
    /// Jumps straight to the exit sequence through the door — useful to see (and regress) the door
    /// opening, Vick's entry and the curtain without playing the whole stage.
    /// <c>KINGSVALLEY_EXIT=N</c> advances N logic ticks; with <c>KINGSVALLEY_EXIT_HOLD=1</c> the
    /// logic freezes after that, so the captured frame is always the same.
    /// </summary>
    private void ApplyExitDiagnostic()
    {
        var raw = Environment.GetEnvironmentVariable("KINGSVALLEY_EXIT");
        if (_exitDiagnostic || raw is null)
            return;

        _exitDiagnostic = true;
        if (!int.TryParse(raw, out int ticks) || ticks < 0)
            return;

        _phase = Phase.Playing;      // without the "Order N!" so the requested state is already on screen
        _phaseTicks = 0;
        _session!.DebugEnterDoor();
        for (int i = 0; i < ticks && _session.Status == StageStatus.Playing; i++)
            _session.Tick(PlayerInput.None);

        _exitHold = Environment.GetEnvironmentVariable("KINGSVALLEY_EXIT_HOLD") == "1";
        var p = _session.Player;
        Console.WriteLine($"[GameScene] KINGSVALLEY_EXIT={ticks} -> phase {_session.Exit}, " +
            $"door frame {_session.DoorFrame}, curtain {_session.CurtainBands}, " +
            $"Vick ({p.X},{p.Y}) pose {p.PoseSet}/{p.Pose} (congela: {_exitHold})");
    }


    private string? Banner() => _phase switch
    {
        Phase.NoData => "NO ROM DATA",
        Phase.Intro => _isDemo ? "DEMO PLAY" : $"STAGE {_run.Stage:D2}",
        Phase.Paused => "PAUSE",
        Phase.GameOver => "GAME OVER",
        _ => null,
    };

    public override void Update()
    {
        base.Update();

        // System menu open: the game freezes (the menu handles its own input).
        if (UI.SystemMenu.IsOpen)
            return;

        bool pauseToggle = Nez.Input.IsKeyPressed(Keys.F1) || Nez.Input.IsKeyPressed(Keys.P);

        bool manualInput = Nez.Input.IsKeyDown(Keys.Left)
            || Nez.Input.IsKeyDown(Keys.Right)
            || Nez.Input.IsKeyDown(Keys.Up)
            || Nez.Input.IsKeyDown(Keys.Down)
            || Nez.Input.IsKeyDown(Keys.A)
            || Nez.Input.IsKeyDown(Keys.D)
            || Nez.Input.IsKeyDown(Keys.W)
            || Nez.Input.IsKeyDown(Keys.S)
            || Nez.Input.IsKeyPressed(Keys.Space)
            || Nez.Input.IsKeyPressed(Keys.Z)
            || Nez.Input.IsKeyPressed(Keys.J)
            || Nez.Input.IsKeyPressed(Keys.K);

        for (int i = 0; i < Nez.Input.GamePads.Length; i++)
        {
            var pad = Nez.Input.GamePads[i];
            if (!pad.IsConnected()) continue;
            if (pad.IsButtonPressed(Buttons.Start)) pauseToggle = true;
            bool padAction = pad.IsButtonPressed(Buttons.A)
                || pad.IsButtonPressed(Buttons.B)
                || pad.IsButtonPressed(Buttons.X)
                || pad.IsButtonPressed(Buttons.Y);
            _actionLatched |= padAction;
            if (padAction || pad.DpadLeftDown || pad.DpadRightDown || pad.DpadUpDown || pad.DpadDownDown ||
                pad.IsLeftStickLeft() || pad.IsLeftStickRight() || pad.IsLeftStickUp() || pad.IsLeftStickDown())
            {
                manualInput = true;
            }
        }

        if (manualInput)
        {
            _navigator.Cancel();
        }

        if (_isDemo)
        {
            bool demoAbort = manualInput || UnifiedPointer.IsJustPressed || UnifiedPointer.IsSecondaryJustPressed
                || Nez.Input.IsKeyPressed(Keys.Escape) || Nez.Input.IsKeyPressed(Keys.Enter)
                || Nez.Input.IsKeyPressed(Keys.Space);

            if (!demoAbort)
            {
                for (int i = 0; i < Nez.Input.GamePads.Length; i++)
                {
                    var pad = Nez.Input.GamePads[i];
                    if (!pad.IsConnected()) continue;
                    if (pad.IsButtonPressed(Buttons.Start) || pad.IsButtonPressed(Buttons.Back) ||
                        pad.IsButtonPressed(Buttons.A) || pad.IsButtonPressed(Buttons.B))
                    {
                        demoAbort = true;
                        break;
                    }
                }
            }

            if (demoAbort)
            {
                GameAudio.StopMusic();
                GameNavigation.GoTo(GameSceneId.MainMenu);
                return;
            }
        }
        else if (UnifiedPointer.IsSecondaryJustPressed)
        {
            // Right mouse button or 2-finger touch:
            // Triggers the player action (throw knife/boomerang, dig with shovel/pickaxe, bore the wall with drill/hammer, or jump)
            _actionLatched = true;
            _navigator.Cancel();
        }
        else if (UnifiedPointer.IsJustPressed)
        {
            var pointer = UnifiedPointer.Position;
            if (pointer.Y < GameConstants.PlayfieldHeight && _session != null && _phase == Phase.Playing)
            {
                var p = _session.Player;
                bool clickedOnPlayer = Math.Abs(pointer.X - (p.X + KingsValley.Core.Gameplay.Physics.ActorSize / 2)) <= KingsValley.Core.Gameplay.Physics.ActorSize &&
                                      Math.Abs(pointer.Y - (p.Y + KingsValley.Core.Gameplay.Physics.ActorSize / 2)) <= KingsValley.Core.Gameplay.Physics.ActorSize;

                // Checks whether an enemy in the current room was clicked
                var enemyTarget = _session.Enemies.FirstOrDefault(e =>
                    e.Alive &&
                    e.Kind != EnemyKind.MovableBlock &&
                    e.Room == _session.CurrentRoom &&
                    Math.Abs(pointer.X - (e.X + KingsValley.Core.Gameplay.Physics.ActorSize / 2)) <= KingsValley.Core.Gameplay.Physics.ActorSize &&
                    Math.Abs(pointer.Y - (e.Y + KingsValley.Core.Gameplay.Physics.ActorSize / 2)) <= KingsValley.Core.Gameplay.Physics.ActorSize);

                if (enemyTarget != null && p.Holding.IsWeapon() && Math.Abs(enemyTarget.Y - p.Y) <= KingsValley.Core.Gameplay.Physics.ActorSize)
                {
                    // With a weapon in hand he clicked an enemy on the same line: turns to it and throws the weapon!
                    p.Facing = enemyTarget.X < p.X ? Facing.Left : Facing.Right;
                    _actionLatched = true;
                    _navigator.Cancel();
                }
                else if (clickedOnPlayer && p.Holding != ItemKind.None)
                {
                    // Click on the character himself with an item in hand = use the item
                    _actionLatched = true;
                    _navigator.Cancel();
                }
                else if (TryHandleToolClick(_session, (int)pointer.X, (int)pointer.Y))
                {
                    // Tool triggered (if already adjacent, uses it right away; if far, navigates there and uses it on arrival)
                }
                else
                {
                    // Click on the map = navigate there
                    _navigator.SetTarget(_session, (int)pointer.X, (int)pointer.Y);
                }
            }
        }

        if (_phase == Phase.NoData || _session == null)
            return;

#if DEBUG
        if (Nez.Input.IsKeyPressed(Keys.N))
        {
            Console.WriteLine($"[Debug] Skip stage via the 'N' shortcut (current stage: {_run.Stage})");
            NextStage();
            return;
        }

        if (Nez.Input.IsKeyPressed(Keys.B))
        {
            Console.WriteLine($"[Debug] Go back a stage via the 'B' shortcut (current stage: {_run.Stage})");
            PreviousStage();
            return;
        }

        if (Nez.Input.IsKeyPressed(Keys.T))
        {
            _debugMusicIndex = (_debugMusicIndex + 1) % 5;
            var musics = new[] { "stage_a", "stage_b", "stage_c", "stage_d", "stage_e" };
            GameAudio.PlayMusic(musics[_debugMusicIndex], loop: true);
            Console.WriteLine($"[Debug] Toggled music via the 'T' shortcut -> {musics[_debugMusicIndex]}");
        }
#endif

        if (pauseToggle)
            _phase = _phase == Phase.Paused ? Phase.Playing : _phase == Phase.Playing ? Phase.Paused : _phase;
        if (_phase == Phase.Playing && Nez.Input.IsKeyPressed(Keys.F5))
            RestartStage();

        _actionLatched |= Nez.Input.IsKeyPressed(Keys.Space)
            || Nez.Input.IsKeyPressed(Keys.Z)
            || Nez.Input.IsKeyPressed(Keys.J)
            || Nez.Input.IsKeyPressed(Keys.K);

        _accumulator += Time.DeltaTime;
        while (_accumulator >= TickSeconds)
        {
            _accumulator -= TickSeconds;
            StepTick();
        }
    }

    private void StepTick()
    {
        switch (_phase)
        {
            case Phase.Intro:
                if (++_phaseTicks >= IntroTicks)
                    _phase = Phase.Playing;
                break;
            case Phase.Playing:
                if (!_exitHold)
                    PlayTick();
                break;
            case Phase.Cleared:
                if (++_phaseTicks >= ClearedTicks)
                    NextStage();
                break;
            case Phase.DeathHold:
                if (++_phaseTicks >= DeathHoldTicks)
                    OnDeath();
                break;
            case Phase.GameOver:
                if (++_phaseTicks >= GameOverTicks)
                {
                    if (GameNavigation.StageOverride != null)
                    {
                        GameNavigation.StageOverride = null;
                        GameNavigation.GoTo(GameSceneId.Editor);
                        return;
                    }
                    GameNavigation.GoTo(GameSceneId.MainMenu);
                }
                break;
        }
    }

    private void PlayTick()
    {
        if (_doorFlashTicks > 0)
            _doorFlashTicks--;

        // Script (documentation/CI or Attract/Demo mode): replaces keyboard and gamepad.
        if (ScriptedInput is { } script)
        {
            _session!.Tick(script.At(_session.TickCount));
            foreach (var e in _session.Events)
                HandleEvent(e);

            if (_isDemo && (_session.TickCount >= script.LastTick || _session.Status != StageStatus.Playing))
            {
                GameAudio.StopMusic();
                GameNavigation.GoTo(GameSceneId.MainMenu);
            }
            return;
        }

        bool left = Nez.Input.IsKeyDown(Keys.Left) || Nez.Input.IsKeyDown(Keys.A);
        bool right = Nez.Input.IsKeyDown(Keys.Right) || Nez.Input.IsKeyDown(Keys.D);
        bool up = Nez.Input.IsKeyDown(Keys.Up) || Nez.Input.IsKeyDown(Keys.W);
        bool down = Nez.Input.IsKeyDown(Keys.Down) || Nez.Input.IsKeyDown(Keys.S);

        for (int i = 0; i < Nez.Input.GamePads.Length; i++)
        {
            var pad = Nez.Input.GamePads[i];
            if (!pad.IsConnected()) continue;
            left |= pad.DpadLeftDown || pad.IsLeftStickLeft();
            right |= pad.DpadRightDown || pad.IsLeftStickRight();
            up |= pad.DpadUpDown || pad.IsLeftStickUp();
            down |= pad.DpadDownDown || pad.IsLeftStickDown();
        }

        bool manualMove = left || right || up || down || _actionLatched;
        PlayerInput input;
        if (manualMove)
        {
            _navigator.Cancel();
            input = new PlayerInput(left, right, up, down, _actionLatched);
        }
        else if (_navigator.HasPath && _session != null)
        {
            var autoInput = _navigator.Update(_session);
            input = new PlayerInput(autoInput.Left, autoInput.Right, autoInput.Up, autoInput.Down, _actionLatched || autoInput.Action);
        }
        else
        {
            input = PlayerInput.None;
        }

        _actionLatched = false;

        foreach (var e in _session!.Tick(input))
            HandleEvent(e);

        if (_session.Status == StageStatus.Cleared)
        {
            _phase = Phase.Cleared;
            _phaseTicks = 0;
        }
        else if (_session.Status == StageStatus.Dead)
        {
            _phase = Phase.DeathHold;
            _phaseTicks = 0;
        }
    }

    private void HandleEvent(GameEvent e)
    {
        GameAudio.Play(e);
        if (e.Kind == GameEventKind.DoorOpened)
            _doorFlashTicks = 8;

        // 4C20 add_score: the extra life by score adds E240 on the same frame (with a jingle);
        // the level HUD draws the _run REST every frame, so switching the run is enough.
        if (e.Kind == GameEventKind.ExtraLife)
            _run = _run.GainLife();
    }

    /// <summary>Input script (KINGSVALLEY_INPUT or Demo) — null in normal play.</summary>
    private InputScript? ScriptedInput
    {
        get
        {
            if (!_scriptLoaded)
            {
                _scriptLoaded = true;
                if (_isDemo)
                {
                    var demoPath = Path.Combine(RomContent.Root, "Demos", $"stage{_run.Stage:D2}.txt");
                    if (!File.Exists(demoPath))
                    {
                        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KingsValley", "extracted", "Demos", $"stage{_run.Stage:D2}.txt");
                        if (File.Exists(appDataPath))
                            demoPath = appDataPath;
                    }
                    if (File.Exists(demoPath))
                    {
                        _script = InputScript.Load(demoPath);
                        Console.WriteLine($"[GameScene] Demo input script: {demoPath} ({_script.LastTick} ticks)");
                    }
                }
                else
                {
                    var path = Environment.GetEnvironmentVariable("KINGSVALLEY_INPUT");
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        _script = InputScript.Load(path);
                        Console.WriteLine($"[GameScene] input script: {path} ({_script.LastTick} ticks)");
                    }
                }
            }

            return _script;
        }
    }

    private bool _scriptLoaded;
    private InputScript? _script;
    private int _debugMusicIndex;

    private void PreviousStage()
    {
        if (GameNavigation.StageOverride != null)
        {
            GameNavigation.StageOverride = null;
            GameNavigation.GoTo(GameSceneId.Editor);
            return;
        }

        int prev = Math.Max(1, _run.Stage - 1);
        _run = GameRun.New(_run.HiScore, prev);
        StartStage();
    }

    private void NextStage()
    {
        if (GameNavigation.StageOverride != null)
        {
            GameNavigation.StageOverride = null;
            GameNavigation.GoTo(GameSceneId.Editor);
            return;
        }

        bool wasLast = _run.IsLastStage;
        int cleared = _run.Stage;
        _run = _run.AddScore(_session!.Score).StageCleared();
        ProfileSaveService.CompleteLevel(_run.Stage - 1, _session.Score);
        ProfileSaveService.UpdateHiScore(_run.HiScore);
        if (wasLast)
        {
            GameNavigation.GoToEnding(_run, StageFx.Ef10At(_run.Stage));   // mode_end (0x4B6E): end of the game + final menu
            return;
        }

        // mode_clear (0x4B06): the 10th stage closes the pyramid (low nibble of the level = 1 in the next one) and
        // the original switches to mode 10 (mode_world) before the next stage.
        if (cleared % 10 == 0)
        {
            GameNavigation.GoToWorldMap(_run);
            return;
        }

        StartStage();
    }

    private void OnDeath()
    {
        if (GameNavigation.StageOverride != null)
        {
            GameNavigation.StageOverride = null;
            GameNavigation.GoTo(GameSceneId.Editor);
            return;
        }

        _run = _run.AddScore(_session!.Score).Died();
        ProfileSaveService.UpdateHiScore(_run.HiScore);
        if (_run.IsGameOver)
        {
            // mode_over (0x4A3D): clean screen + "game  over" + "f5  continue".
            GameNavigation.GoToGameOver(_run);
            return;
        }

        StartStage();
    }

    private bool TryHandleToolClick(StageSession session, int clickX, int clickY)
    {
        var p = session.Player;
        var tool = p.Holding;
        if (tool is not (ItemKind.Shovel or ItemKind.Pickaxe or ItemKind.Hammer or ItemKind.Drill))
            return false;

        var map = session.Map;
        int clickedCol = RoomMap.FloorDiv(clickX);
        int clickedRow = RoomMap.FloorDiv(clickY);

        Facing clickDir = clickX < (p.X + KingsValley.Core.Gameplay.Physics.ActorSize / 2) ? Facing.Left : Facing.Right;

        // 1. Direct check: if the player is already in front of a breakable wall or diggable floor
        if (tool.DigsWall())
        {
            // Hammer / Drill: checks whether there is a Soft wall in front of the player
            int frontX = clickDir == Facing.Right ? p.X + KingsValley.Core.Gameplay.Physics.ActorSize + 4 : p.X - 4;
            int frontCol = RoomMap.FloorDiv(frontX);
            int bodyRow1 = RoomMap.FloorDiv(p.Y);
            int bodyRow2 = RoomMap.FloorDiv(p.Y + 8);

            bool softAhead = frontCol >= 0 && frontCol < GameConstants.RoomColumns &&
                            (map.Get(frontCol, bodyRow1) == TerrainKind.Soft || map.Get(frontCol, bodyRow2) == TerrainKind.Soft);

            // If it clicked toward the wall ahead (within sight/wall range)
            if (softAhead && Math.Abs(clickedRow - bodyRow1) <= 3)
            {
                p.Facing = clickDir;
                _actionLatched = true;
                _navigator.Cancel();
                return true;
            }
        }
        else if (tool.DigsFloor())
        {
            // Shovel / Pickaxe: checks whether there is a Soft block under the feet ahead
            int feetY = p.Y + KingsValley.Core.Gameplay.Physics.ActorSize;
            int floorRow = RoomMap.FloorDiv(feetY);
            int floorX = clickDir == Facing.Right ? p.X + 8 : p.X - 8;
            int floorCol = RoomMap.FloorDiv(floorX);

            bool softBelow = floorRow < GameConstants.RoomRows && floorCol >= 0 && floorCol < GameConstants.RoomColumns &&
                             map.Get(floorCol, floorRow) == TerrainKind.Soft;

            if (softBelow && (Math.Abs(clickedRow - floorRow) <= 2 || Math.Abs(clickedCol - floorCol) <= 2))
            {
                p.Facing = clickDir;
                _actionLatched = true;
                _navigator.Cancel();
                return true;
            }
        }

        // 2. If not adjacent, look for the Soft block nearest to the click and the reachable outer faces
        (int col, int row)? softTarget = null;
        int[] probeRows = { 0, 1, -1, 2, -2 };
        int[] probeCols = { 0, -1, 1, -2, 2 };
        foreach (int dr in probeRows)
        {
            foreach (int dc in probeCols)
            {
                int c = clickedCol + dc;
                int r = clickedRow + dr;
                if (c >= 0 && c < GameConstants.RoomColumns && r >= 0 && r < GameConstants.RoomRows &&
                    map.Get(c, r) == TerrainKind.Soft)
                {
                    softTarget = (c, r);
                    break;
                }
            }
            if (softTarget != null) break;
        }

        if (softTarget == null)
            return false;

        int softCol = softTarget.Value.col;
        int softRow = softTarget.Value.row;

        var candidates = new List<(int standX, int standY, Facing facing)>();

        if (tool.DigsFloor())
        {
            // Shovel / Pickaxe: digs the floor under the feet
            int standY = (softRow - 2) * GameConstants.CellSize;
            candidates.Add(((softCol - 1) * GameConstants.CellSize, standY, Facing.Right));
            candidates.Add((softCol * GameConstants.CellSize, standY, Facing.Left));
            candidates.Add(((softCol + 1) * GameConstants.CellSize, standY, Facing.Left));
        }
        else if (tool.DigsWall())
        {
            // Hammer / Drill: looks for the reachable outer face of the Soft wall
            int wallY = (softRow & ~1) * GameConstants.CellSize;

            // Right face (the player ends up on the right of the wall, looking left)
            for (int c = softCol; c < GameConstants.RoomColumns - 2; c++)
            {
                if (map.Get(c, softRow) == TerrainKind.Soft && map.Get(c + 1, softRow) != TerrainKind.Soft)
                {
                    candidates.Add(((c + 1) * GameConstants.CellSize, wallY, Facing.Left));
                    break;
                }
            }

            // Left face (the player ends up on the left of the wall, looking right)
            for (int c = softCol; c >= 2; c--)
            {
                if (map.Get(c, softRow) == TerrainKind.Soft && map.Get(c - 1, softRow) != TerrainKind.Soft)
                {
                    candidates.Add(((c - 2) * GameConstants.CellSize, wallY, Facing.Right));
                    break;
                }
            }
        }

        if (candidates.Count == 0)
            return false;

        // If the player is already in one of the valid positions:
        foreach (var c in candidates)
        {
            if (Math.Abs(p.X - c.standX) <= 12 && Math.Abs(p.Y - c.standY) <= 8)
            {
                p.Facing = c.facing;
                _actionLatched = true;
                _navigator.Cancel();
                return true;
            }
        }

        // If he needs to walk a bit to get there: finds the best route to the reachable face
        List<NavWaypoint>? bestPath = null;
        (int standX, int standY, Facing facing)? bestCandidate = null;

        foreach (var c in candidates)
        {
            int standCol = RoomMap.FloorDiv(c.standX);
            int standR = RoomMap.FloorDiv(c.standY);
            if (standCol < 0 || standCol > GameConstants.RoomColumns - 2 || standR < 0 || standR > GameConstants.RoomRows - 2)
                continue;

            var path = RoomPathfinder.FindPath(map, p.X, p.Y, c.standX, c.standY, canJump: false, doors: session.GetRevolvingDoorPassages());
            if (path != null && (bestPath == null || path.Count < bestPath.Count))
            {
                bestPath = path;
                bestCandidate = c;
            }
        }

        if (bestCandidate != null && bestPath != null)
        {
            var chosen = bestCandidate.Value;
            _navigator.SetTarget(session, chosen.standX, chosen.standY);
            _navigator.OnArrival = s =>
            {
                s.Player.Facing = chosen.facing;
                _actionLatched = true;
            };
            return true;
        }

        return false;
    }

    public override void OnGfxVariantChanged()
    {
        _roomBackgrounds?.Reset();
    }
}
