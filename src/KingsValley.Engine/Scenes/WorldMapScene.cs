#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Interlude between pyramids — the original's <c>mode_world</c> (0x4B4C → <c>world_tick</c> 0x6F5D).
///
/// In the ROM, when the 10th stage of a pyramid ends <c>mode_clear</c> (0x4B06) switches to mode 10:
/// <c>world_script</c> (0x6F63) runs an 11-step script in <c>E257</c> —
/// <c>world_scr</c> (assembles the map, plays the map BGM in <c>sfx_0d</c> = id 0x0D and waits 0x3C ticks),
/// <c>world_hold</c>, <c>world_tour</c> (Vick walks along the path), <c>world_pic0/1/2</c> (the three
/// engravings), <c>world_out</c>, <c>world_count</c>, <c>world_sat</c>, <c>world_in</c> and
/// <c>world_exit</c>; at the end <c>goto_stage</c>.
///
/// The port shows the map screen with the map music and **animates Vick's tour** with the ROM data
/// (<see cref="WorldTour"/>: path table 0xBA57, lengths 0x7177, initial positions 0x71CA and
/// the deltas of <c>world_delta</c> 0x7140, one step every 8 ticks). The three engravings <c>wpic0..2</c>
/// (art of bank EF) have not been extracted yet — see <c>docs/en/ROM_ENGINEERING.md</c>.
/// </summary>
public class WorldMapScene : SceneBase
{
    private readonly GameRun _run;
    private readonly bool _intro;

    public WorldMapScene(GameRun run) => (_run, _intro) = (run, false);

        /// <summary>Opening cutscene (Vick walks to the pyramid + map "DO YOUR BEST !!").</summary>
    public WorldMapScene(GameRun run, bool intro) => (_run, _intro) = (run, intro);

    protected override void OnInitialize()
    {
        GameAudio.Initialize();
        GameAudio.PlayWorldMap();
        // Door presentation uses the shared player atlas. Load it before the timed scene and
        // before SpriteBatch drawing; first-use texture upload must not stall the entry frames.
        if(_intro && RomContent.Flavor==ArtFlavor.Msx1) _=GameAssets.Kv2;

        var screenName = _intro ? "pyramid" : "world_map";
        var path = RomContent.ScreenPath(screenName);
        if (!System.IO.File.Exists(path) && !_intro)
            path = RomContent.ScreenPath("pyramid");
        var screen = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, path);

        var vickPath = RomContent.ScreenPath("world_vick");
        var vick = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, vickPath);

        // Opening: the map with the stamped message ("DO YOUR BEST !!"), the pyramid screen and the
        // Vick sprite in 5 16x32 poses with the ROM colors (blue from palette 13 + skin from 14).
        Texture2D? introMsg = null;
        Texture2D? introVick = null;
        if (_intro)
        {
            introMsg = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, RomContent.ScreenPath("intro_msg"));
            introVick = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, RomContent.ScreenPath("intro_vick"));
        }

        AddEntity(new Entity("worldmap"))
            .AddComponent(new WorldMapRenderer(screen, vick, introMsg, introVick, _run, _intro));
    }

    public override void OnGfxVariantChanged()
    {
        var renderer = FindEntity("worldmap")?.GetComponent<WorldMapRenderer>();
        renderer?.ReloadGraphics();
    }

    private sealed class WorldMapRenderer : RenderableComponent, IUpdatable
    {
        private Texture2D? _screen;
        private Texture2D? _vick;
        private Texture2D? _introMsg;
        private Texture2D? _introVick;
        private NativeActorAnimation? _nativeIntro;
        private bool _loggedIntroDoor;
        private readonly GameRun _run;
        private readonly bool _intro;
        private int _world;
        private float _duration;
        private float _time;
        private float _tickAccum;
        private int _ticks;
        private int _step;
        private int _introTick;

        /// <summary>One ROM logic call = 1 tick (the ISR is held at E206) = 2/60 s.</summary>
        private const float TickSeconds = 2f / 60f;

        // --- Opening: everything measured in openMSX (SAT dump + palette at 30 Hz) ---
        // Vick walks 1 px/tick (30 px/s) from the left to the door at X=216; the path goes up and
        // down the base of the pyramid (Y 168 -> 142 -> 160). Poses 16/24/32 of the sprite bank,
        // 8 ticks each (16, 24, 32, 24); at the door he keeps poses 0/80 and fades out until
        // disappearing (palette 13/14 -> black, 29.7 s to 32.2 s in the emulator).
        private const int IntroArriveX = 216;
        private const int IntroArriveY = 160;
        private const int IntroFadeStart = 245;
        private const int IntroFadeEnd = 321;
        private const int IntroHideTick = 338;
        private const int IntroMsgEnd = 398;

        /// <summary>Y of Vick per tick (measured; linear between the points).</summary>
        private static readonly (int Tick, int Y)[] IntroPath =
        [
            (0, 168), (96, 168), (109, 142), (115, 142), (116, 143), (126, 162), (127, IntroArriveY), (IntroHideTick, IntroArriveY),
        ];

        /// <summary>Order of the walking poses (same cycle as the game: <see cref="PlayerPoses.WalkRight"/>).</summary>
        private static readonly int[] WalkPoses = [0, 1, 2, 1];

        public void ReloadGraphics()
        {
            _screen?.Dispose();
            _vick?.Dispose();
            _introMsg?.Dispose();
            _introVick?.Dispose();

            var screenName = _intro ? "pyramid" : "world_map";
            var path = RomContent.ScreenPath(screenName);
            if (!System.IO.File.Exists(path) && !_intro)
                path = RomContent.ScreenPath("pyramid");
            _screen = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, path);

            var vickPath = RomContent.ScreenPath("world_vick");
            _vick = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, vickPath);

            if (_intro)
            {
                _introMsg = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, RomContent.ScreenPath("intro_msg"));
                _introVick = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, RomContent.ScreenPath("intro_vick"));
            }
            LoadNativeIntro();
        }

        private void LoadNativeIntro()=>_nativeIntro=_intro && RomContent.Flavor==ArtFlavor.Msx1
            ?NativeActorAnimation.Load(RomContent.ScreenMetadataPath("intro_vick")):null;

        public WorldMapRenderer(Texture2D? screen, Texture2D? vick, Texture2D? introMsg, Texture2D? introVick,
            GameRun run, bool intro)
        {
            _screen = screen;
            _vick = vick;
            _introMsg = introMsg;
            _introVick = introVick;
            _run = run;
            _intro = intro;
            LoadNativeIntro();

            // E241 = world; the interlude happens after closing 10 stages.
            _world = Math.Clamp((run.Stage - 1) / 10 + 1, 1, 5);

            // world_scr waits 0x3C ticks and the tour moves 8 ticks per step (world_step).
            // (the opening does not use the duration: it has its own timeline in ticks)
            _duration = (60 + WorldTour.StepsOf(_world) * 8) * TickSeconds;
        }

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        public void Update()
        {
            // Keys 1..5 to inspect the paths of any pyramid/world in DEBUG
            if (!_intro)
            {
                for (int w = 1; w <= 5; w++)
                {
                    if (Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.D0 + w)
                        || Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.NumPad0 + w))
                    {
                        _world = w;
                        _step = 0;
                        _ticks = 0;
                        _time = 0;
                        _duration = (60 + WorldTour.StepsOf(_world) * 8) * TickSeconds;
                        GameAudio.PlayConfirm();
                    }
                }
            }

            _time += Time.DeltaTime;

            if (_intro)
            {
                UpdateIntro();
                return;
            }

            // The ROM tour: world_step writes E204=8 and moves 1 unit every 8 ticks.
            _tickAccum += Time.DeltaTime;
            while (_tickAccum >= TickSeconds)
            {
                _tickAccum -= TickSeconds;
                _ticks++;
                if (_ticks % 8 == 0 && _step < WorldTour.StepsOf(_world))
                    _step++;
            }

            int maxSteps = WorldTour.StepsOf(_world);
            if (SkipPressed())
            {
                if (_step < maxSteps)
                {
                    // Jumps straight to the destination pyramid (as in 0x7009 of the ROM: loads 0x71D4)
                    _step = maxSteps;
                }
                else
                {
                    GameNavigation.GoToGame(_run);
                    Entity?.Destroy();
                    return;
                }
            }

            if (_time >= _duration)
            {
                GameNavigation.GoToGame(_run);
                Entity?.Destroy();
            }
        }

        /// <summary>Opening: Vick's walk (measured in the emulator) and then the map with the message.</summary>
        private void UpdateIntro()
        {
            if(_nativeIntro is {} native)
            {
                if(SkipPressed())
                {
                    if(native.At(_time).Background==0)
                    {
                        int card=native.Frames.FindIndex(f=>f.Background==1);
                        _time=card/(float)native.FramesPerSecond;
                    }
                    else {GameNavigation.GoToGame(_run);Entity?.Destroy();}
                    return;
                }
                if(_time*native.FramesPerSecond>=native.Frames.Count){GameNavigation.GoToGame(_run);Entity?.Destroy();}
                return;
            }
            _tickAccum += Time.DeltaTime;
            while (_tickAccum >= TickSeconds)
            {
                _tickAccum -= TickSeconds;
                _introTick++;
            }

            // In the ROM the map darkens after the arrival (palette 13/14 -> black) and the map screen
            // ("DO YOUR BEST !!") comes in next; any key skips the current phase.
            if (SkipPressed())
            {
                if (_introTick < IntroHideTick)
                    _introTick = IntroHideTick;
                else
                {
                    GameNavigation.GoToGame(_run);
                    Entity?.Destroy();
                }
                return;
            }

            if (_introTick >= IntroMsgEnd)
            {
                GameNavigation.GoToGame(_run);
                Entity?.Destroy();
            }
        }

        /// <summary>Space/Enter/Esc, a click or the joystick button (0x6FFD: <c>AND 0x30</c>).</summary>
        private static bool SkipPressed()
        {
            bool skip = Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter)
                || Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Space)
                || Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Escape)
                || KingsValley.Engine.Input.UnifiedPointer.IsJustPressed;

            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                skip |= pad.IsButtonPressed(Microsoft.Xna.Framework.Input.Buttons.A)
                    || pad.IsButtonPressed(Microsoft.Xna.Framework.Input.Buttons.Start);
            }

            return skip;
        }

        /// <summary>Y of Vick on the requested tick (linear interpolation between the measurements).</summary>
        private static int IntroYAt(int tick)
        {
            var path = IntroPath;
            for (int i = 1; i < path.Length; i++)
            {
                var (t0, y0) = path[i - 1];
                var (t1, y1) = path[i];
                if (tick > t1)
                    continue;
                if (t1 == t0)
                    return y1;
                return y0 + (int)MathF.Round((tick - t0) * (y1 - y0) / (float)(t1 - t0));
            }

            return path[^1].Y;
        }

        private void RenderIntro(Batcher batcher)
        {
            if(_nativeIntro is {} native)
            {
                var sample=native.At(_time);var background=sample.Background==1?_introMsg:_screen;
                int offset=background==null?10:(GameConstants.DesignHeight-background.Height)/2;
                if(background!=null)batcher.Draw(background,new Vector2(0,offset),Color.White);
                string? doorSprite=Msx1IntroPresentation.DoorSprite(sample.Pose);
                if(doorSprite!=null)
                {
                    int doorX=Msx1IntroPresentation.DoorX(sample.Pose,sample.X);
                    if(!_loggedIntroDoor)
                    {
                        var sprite=MsxScreen.Get(doorSprite);
                        Console.WriteLine($"[Intro] Back-facing door: {doorSprite}, source={sprite?.SourceRect}, position=({doorX},{sample.Y+offset}), atlas={RomContent.AtlasPath}");
                        _loggedIntroDoor=true;
                    }
                    MsxScreen.Draw(batcher,doorSprite,doorX,sample.Y+offset-Msx1IntroPresentation.DoorTopPadding);
                }
                else if(sample.Pose>=0 && _introVick!=null)
                    batcher.Draw(_introVick,new Vector2(sample.X,sample.Y+offset),new Rectangle(sample.Pose*16,0,16,32),Color.White);
                return;
            }
            // After the fade the screen is the valley map with "DO YOUR BEST !!" (ROM bitmap).
            if (_introTick >= IntroHideTick && _introMsg != null)
            {
                int offsetYMsg = Math.Max(0, (GameConstants.DesignHeight - _introMsg.Height) / 2);
                batcher.Draw(_introMsg, new Vector2(0, offsetYMsg), Color.White);
                return;
            }

            int offsetY = _screen != null ? Math.Max(0, (GameConstants.DesignHeight - _screen.Height) / 2) : 0;
            if (_screen != null)
                batcher.Draw(_screen, new Vector2(0, offsetY), Color.White);

            if (_introVick == null)
                return;

            int x = Math.Min(1 + _introTick, IntroArriveX);
            int y = IntroYAt(_introTick) + offsetY;
            bool arrived = x >= IntroArriveX;

            // The ROM switches to the door set when he touches the portal (poses 3/4 of the .bin).
            int frame = arrived
                ? 3 + ((_introTick / 8) % 2)
                : WalkPoses[(_introTick / 8) % WalkPoses.Length];

            // The ROM fades Vick out (palette 13/14 -> black) before switching to the map.
            float fade = 1f;
            if (_introTick > IntroFadeStart)
                fade = 1f - Math.Clamp((_introTick - IntroFadeStart) / (float)(IntroFadeEnd - IntroFadeStart), 0f, 1f);

            // 16x32, with the SAT Y being the top (longplay: feet at Y+15).
            batcher.Draw(_introVick, new Vector2(x, y), new Rectangle(frame * 16, 0, 16, 32),
                new Color(fade, fade, fade));
        }

        public override void Render(Batcher batcher, Camera camera)
        {
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);

            if (_intro)
            {
                RenderIntro(batcher);
                return;
            }

            if (_screen != null)
            {
                int offsetY = Math.Max(0, (GameConstants.DesignHeight - _screen.Height) / 2);
                batcher.Draw(_screen, new Vector2(0, offsetY), Color.White);
            }

            // As in the ROM (0x7159 / 0xBDD7 / 0xBE01): in the pyramid's 3D corridor (on the right),
            // Vick walks backwards alternating the steps every 7 ticks (SAT patterns 4 and 52 at X=168, Y=104)
            if (_vick != null)
            {
                int frame = (_ticks / 7) % 2;
                var src = new Rectangle(frame * 32, 0, 32, 48);
                batcher.Draw(_vick, new Vector2(168, 104), src, Color.White);
            }

            // In the left maze (0xBDDA): VDP SAT sprite 12 (Screen 5, pattern 0xD0, color 7 = Red).
            // It is a solid 2x2 pixel dot that runs along the yellow trails continuously.
            // On reaching the destination pyramid (steps completed), it blinks softly to highlight the pyramid.
            var (px, py, _) = WorldTour.StateAt(_world, _step);
            int maxSteps = WorldTour.StepsOf(_world);
            bool arrivedTour = _step >= maxSteps;
            bool drawPoint = !arrivedTour || ((_ticks / 8) % 2 == 0);

            if (drawPoint)
            {
                MsxScreen.Fill(batcher, new Rectangle(px, py, 2, 2), Color.Red);
            }

            int stage = ((_world - 1) * 10) % 10 + 1;
            MsxScreen.Text(batcher, $"PYRAMID {_world}  STAGE {stage}", 60, 8, Color.White);
        }
    }
}
