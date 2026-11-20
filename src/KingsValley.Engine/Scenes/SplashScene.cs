#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Opening: Konami logo drawn like the original (line-by-line reveal), the wait and
/// then the <b>Japanese title</b> — and then the menu. Any key skips.
///
/// ## Numbers (measured in the ROM/emulator — not invented)
///
/// - `title_load` (bank 0, 0x5B6A) loads the art into VRAM and zeroes E2C0=60/E2C1=49.
/// - `wmap_slide` (0x5BC8) copies the art with **HMMM** from "page 1" (y=320) to the screen
///   (y=64) with increasing height (NY = 1..48), **one row every 2 frames of 60 Hz**
///   (the `and 001h` gate on E2C0) → 48 rows in **1.6 s**.
/// - Step 49 of `wmap_slide` (2 frames after the last copy, through the parity gate) only
///   sets E2C2 = 1; `boot_clear` (0x4742) then counts **256 decrements** of E204 (0 → 0xFF..0x00)
///   before turning the screen off and building the title → **258 frames ≈ 4.3 s** with the full logo.
/// - `title_jp` (0x5F8D, called by `boot_clear`): `scr_reset` + stamp of `stamp_logo_jp` — it is the
///   <b>Japanese title</b>, and there is no fade: the screen is turned off and on again (hard cut).
/// - `boot_done` (0x475C) runs `hud_meter` (0x5FC6): the GAME/EDIT box comes in over **0x39 steps,
///   one every 2 frames** (even `E203` gate) = 114 frames ≈ 1.9 s; then comes the menu (`mode_hold`).
///
/// The pyramid (screen `pyramid.bin`) is <b>not</b> part of the boot — it is the cutscene of Vick walking
/// to the pyramid before stage 1 (`WorldMapScene` with `intro: true`).
///
/// The logo art is the 168x48 crop at (40,320) of the static-state VRAM dump (the staging copy
/// that HMMM takes to (40,64)), with the game's own palette: #FF6D00, #DA2400 and #919191.
/// The title comes from the stable frame of the dump (`title.bin`, assembled at runtime by `ScreenBuilder`) —
/// with the menu box already drawn, so the box sliding (`hud_meter`) is not animated here.
/// </summary>
public class SplashScene : SceneBase
{
    private const float FastBootSeconds = 0.15f;

    /// <summary>Height of the logo art (y=64..111 on the MSX2 screen).</summary>
    private const int LogoLines = 48;

    /// <summary>One row every 2 frames of 60 Hz (`wmap_slide` gate).</summary>
    private const float SecondsPerLine = 2f / GameConstants.FramesPerSecond;

    /// <summary>48 rows x 2 frames = 1.6 s of reveal.</summary>
    private const float LogoRevealSeconds = LogoLines * SecondsPerLine;

    /// <summary>
    /// Wait with the full logo: 2 frames of the parity gate (E2C2 on the 49th step) +
    /// 256 decrements of E204 in `boot_clear` = 258 frames ≈ 4.3 s.
    /// </summary>
    private const float LogoHoldSeconds = 258f / GameConstants.FramesPerSecond;

    /// <summary>Position of the art on screen (same as HMMM: SX=40, SY=64).</summary>
    private const int LogoX = 40;
    private const int LogoY = 64;

    /// <summary>
    /// `boot_done` → `hud_meter`: the GAME/EDIT box comes in over 0x39 steps of 2 frames
    /// (E203 parity gate) = 114 frames ≈ 1.9 s before the menu accepts input.
    /// </summary>
    private const float TitleSeconds = 0x39 * 2f / GameConstants.FramesPerSecond;

    protected override void OnInitialize()
    {
        var fast = System.Environment.GetEnvironmentVariable("KINGSVALLEY_FAST_BOOT") == "1";

        var logoPath = RomContent.ScreenPath("konami");
        var logo = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, logoPath);

        // title_jp (0x5F8D): the Japanese title that boot_clear builds after the logo.
        var screenPath = RomContent.ScreenPath("title");
        var screen = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, screenPath);

        AddEntity(new Entity("splash")).AddComponent(new SplashRenderer(fast, logo, screen));
    }

    public override void OnGfxVariantChanged()
    {
        var renderer = FindEntity("splash")?.GetComponent<SplashRenderer>();
        renderer?.ReloadGraphics();
    }

    private sealed class SplashRenderer : RenderableComponent, IUpdatable
    {
        private readonly bool _fast;
        private Texture2D? _logo;
        private Texture2D? _screen;
        private float _time;

        public SplashRenderer(bool fast, Texture2D? logo, Texture2D? screen)
        {
            _fast = fast;
            _logo = logo;
            _screen = screen;
        }

        public void ReloadGraphics()
        {
            _logo?.Dispose();
            _screen?.Dispose();
            var logoPath = RomContent.ScreenPath("konami");
            _logo = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, logoPath);
            var screenPath = RomContent.ScreenPath("title");
            _screen = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, screenPath);
        }

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        private float LogoDuration => _fast ? FastBootSeconds : LogoRevealSeconds + LogoHoldSeconds;
        private float TitleDuration => _fast ? FastBootSeconds : TitleSeconds;
        private float TotalDuration => LogoDuration + TitleDuration;

        public void Update()
        {
            _time += Time.DeltaTime;
            bool skip = Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter)
                || Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Space)
                || KingsValley.Engine.Input.UnifiedPointer.IsJustPressed;

            if (!skip)
            {
                for (int i = 0; i < Nez.Input.GamePads.Length; i++)
                {
                    var pad = Nez.Input.GamePads[i];
                    if (pad.IsConnected() && (pad.IsButtonPressed(Microsoft.Xna.Framework.Input.Buttons.A) || pad.IsButtonPressed(Microsoft.Xna.Framework.Input.Buttons.Start)))
                    {
                        skip = true;
                        break;
                    }
                }
            }

            if (skip)
                _time = TotalDuration;

            if (_time >= TotalDuration)
            {
                GameNavigation.GoTo(GameSceneId.MainMenu);
                Entity?.Destroy();
            }
        }

        public override void Render(Batcher batcher, Camera camera)
        {
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);

            if (_time < LogoDuration)
            {
                RenderLogo(batcher);
                return;
            }

            // title_jp (0x5F8D): the Japanese title comes in with a hard cut (scr_off/scr_reset/scr_on) and
            // stays while hud_meter slides the GAME/EDIT box (0x39 steps of 2 frames).
            if (_screen != null)
            {
                int offsetY = (GameConstants.DesignHeight - _screen.Height) / 2;
                batcher.Draw(_screen, new Vector2(0, Math.Max(0, offsetY)), Color.White);
            }
            else
                MsxScreen.Text(batcher, "KINGS VALLEY II", 68, 100, Color.White);
        }

        /// <summary>
        /// Draws the first N rows of the art — the equivalent of the original's increasing-height
        /// HMMM (NY = 1..48, one row every 2 frames).
        /// </summary>
        private void RenderLogo(Batcher batcher)
        {
            if (_logo == null)
            {
                MsxScreen.Text(batcher, "KONAMI", 104, 100, Color.White);
                return;
            }

            int lines = _fast
                ? _logo.Height
                : Math.Clamp((int)(_time / SecondsPerLine), 0, _logo.Height);

            if (lines <= 0)
                return;

            int logoY = RomContent.Flavor == ArtFlavor.Msx1
                ? Math.Max(0, (GameConstants.DesignHeight - _logo.Height) / 2)
                : LogoY;

            var source = new Rectangle(0, 0, _logo.Width, lines);
            batcher.Draw(_logo, new Vector2(LogoX, logoY), source, Color.White, 0f, Vector2.Zero,
                Vector2.One, SpriteEffects.None, 0f);
        }
    }
}
