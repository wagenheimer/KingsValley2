#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// **Game over** — the original's <c>mode_over</c> (0x4A3D). The ROM clears the screen (<c>scr_reset</c>) and
/// prints two streams from bank 0:
/// <list type="bullet">
/// <item><c>txt_over</c> (0x5F4C): <c>[0x58][0x58]</c> = (88,88) — <b>"game  over"</b> (two spaces,
/// as in the original);</item>
/// <item><c>txt_cont</c> (0x5F59): <c>[0x50][0x68]</c> = (80,104) — <b>"f5  continue"</b>, printed
/// **only when there is a continue** (<c>E217 | E254</c>);</item>
/// </list>
/// Then comes the <c>over_hud</c> (0x4C8A, the MSX2 box with the scores) and the wait: <c>0x78</c> = 120
/// ticks without continue, <c>0xB4</c> = 180 ticks with continue; any key decides (<c>F5</c> continues,
/// otherwise it goes back to the title — <c>over_wait</c> → <c>title_reset</c>).
///
/// In the port: <b>F5</b> (or Space/Enter) resumes the run on the same stage with the starting lives; with no
/// choice, at the end of the window it goes to the menu.
/// </summary>
public class GameOverScene : SceneBase
{
    /// <summary>ROM window without a loading device (<c>0x78</c> = 120 ticks at ~30 Hz).</summary>
    private const int WindowTicks = 120;

    private readonly GameRun _run;

    public GameOverScene(GameRun run) => _run = run;

    protected override void OnInitialize()
    {
        GameAudio.Initialize();
        GameAudio.StopMusic();
        GameAudio.StopAllEffects();
        AddEntity(new Entity("gameover")).AddComponent(new GameOverRenderer(_run));
    }

    private sealed class GameOverRenderer : RenderableComponent, IUpdatable
    {
        private readonly GameRun _run;
        private int _ticks;
        private float _accum;
        private bool _continueChosen;

        /// <summary>1 ROM logic tick = 2/60 s.</summary>
        private const float TickSeconds = 2f / 60f;

        public GameOverRenderer(GameRun run) => _run = run;

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        public void Update()
        {
            if (Nez.Input.IsKeyPressed(Keys.Escape))
            {
                GameNavigation.GoTo(GameSceneId.MainMenu);
                Entity?.Destroy();
                return;
            }

            bool confirm = Nez.Input.IsKeyPressed(Keys.F5) || Nez.Input.IsKeyPressed(Keys.Space)
                || Nez.Input.IsKeyPressed(Keys.Enter) || KingsValley.Engine.Input.UnifiedPointer.IsJustPressed;

            // mode_over only prints/accepts the continue when E217|E254 != 0 — on the cartridge that depends
            // on the TRYAGAIN cheat (pwd_cheat 0x6B24, E217 = 1).
            if (confirm && !_continueChosen)
            {
                if (_run.ContinueEnabled)
                {
                    _continueChosen = true;
                    GameAudio.PlayConfirm();
                    // over_cont → goto_stage: score zerado e E240 = 1 (play_init do 6B24).
                    GameNavigation.GoToGame(GameRun.New(_run.HiScore, _run.Stage, rest: 1,
                        _run.Invincible, continueEnabled: true));
                    Entity?.Destroy();
                    return;
                }
                else
                {
                    // Without a continue, a key advances straight to the menu (over_wait → title_reset).
                    GameNavigation.GoTo(GameSceneId.MainMenu);
                    Entity?.Destroy();
                    return;
                }
            }

            _accum += Time.DeltaTime;
            while (_accum >= TickSeconds)
            {
                _accum -= TickSeconds;
                if (++_ticks >= WindowTicks)
                {
                    GameNavigation.GoTo(GameSceneId.MainMenu);
                    Entity?.Destroy();
                    return;
                }
            }
        }

        public override void Render(Batcher batcher, Camera camera)
        {
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);

            // txt_over 0x5F4C: "game  over" em (0x58,0x58).
            MsxScreen.Text(batcher, "GAME  OVER", 88, 88, Color.White);

            // txt_cont 0x5F59: "f5  continue" at (0x50,0x68) — the ROM only prints it when there is a continue.
            if (_run.ContinueEnabled)
                MsxScreen.Text(batcher, "F5  CONTINUE", 80, 104, Color.Gold);

            // over_hud (0x4C8A): os placares do MSX2.
            MsxScreen.Text(batcher, $"HISCORE {_run.HiScore:D6}   SCORE {_run.Score:D6}", 16, 160, Color.White);
            MsxScreen.Text(batcher, $"STAGE {_run.Stage:D2}", 16, 176, Color.White);

            string hint = _run.ContinueEnabled ? "F5/SPACE: CONTINUE  ESC: MENU" : "SPACE/ESC: MENU";
            MsxScreen.Text(batcher, hint, (GameConstants.DesignWidth - hint.Length * 8) / 2f, 196, Color.Gray);
        }
    }
}
