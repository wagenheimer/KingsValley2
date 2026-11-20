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
/// End of the game — <c>mode_end</c> + <c>mode_endtxt</c> (0x4BAF) of the original, in the ROM's real order:
/// <list type="number">
/// <item><c>end_text</c> (0x4B9C): <c>scr_reset</c> + <c>ld a,078h / call sub_delay</c> → 120 ticks
/// of clean screen, then table <c>0x9D37</c> (bank 14) prints the pyramid name:
/// <c>EF10=1</c> → <b>MUSIC STAGE</b> (80,80); <c>EF10=2</c> → <b>PUZZLE STAGE</b> (72,80);</item>
/// <item>the <c>end_script</c> script (0x7353, banks 1-3) calls <c>print_12</c> for the five pages of
/// text of bank 0x0C: <c>0xACC3</c>, <c>0xAD12</c>, <c>0xAD58</c> ("push space key"), <c>0xAEDD</c>
/// and <c>0xAF21</c> — see <see cref="EndingTexts"/>;</item>
/// <item>finally the port's card (score) and the final menu (<c>end_menu</c>).</item>
/// </list>
/// </summary>
public class EndingScene : SceneBase
{
    /// <summary>Time of each text page (the ROM waits for the key; here there is auto-advance).</summary>
    private const float PageSeconds = 5f;

    /// <summary>120 ticks (~4 s at 30 Hz): the <c>ld a,078h</c> of <c>end_text</c> before the text.</summary>
    private const float BlankSeconds = 4f;

    private readonly GameRun _run;

    /// <summary><c>EF10</c> do ROM: 1 = MUSIC STAGE, 2 = PUZZLE STAGE.</summary>
    private readonly int _pyramid;

    public EndingScene(GameRun run, int pyramid = 1)
    {
        _run = run;
        _pyramid = pyramid is 1 or 2 ? pyramid : 1;
    }

    protected override void OnInitialize()
    {
        GameAudio.Initialize();
        GameAudio.PlayEnding();
        AddEntity(new Entity("ending")).AddComponent(new EndingRenderer(_run, _pyramid));
    }

    private sealed class EndingRenderer : RenderableComponent, IUpdatable
    {
        /// <summary>1 = pyramid name, 2..6 = ending texts, 7 = card/score.</summary>
        private int _phase = 1;
        private float _time;

        private static readonly EndingTexts.Line[][] Pages =
        [
            EndingTexts.Boot,
            EndingTexts.Text2,
            EndingTexts.Text3,
            EndingTexts.Text4,
            EndingTexts.Text5,
        ];

        private readonly GameRun _run;
        private readonly int _pyramid;

        public EndingRenderer(GameRun run, int pyramid)
        {
            _run = run;
            _pyramid = pyramid;
        }

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        /// <summary>Text and position from table 0x9D37 (bank 14 → file 0x1DD37).</summary>
        private static (string Text, int X) StageText(int pyramid) => pyramid == 2
            ? ("PUZZLE STAGE", 72)     // 0x9D49: [0x48][0x50] = (72,80)
            : ("MUSIC STAGE", 80);     // 0x9D3B: [0x50][0x50] = (80,80)

        private static bool SkipPressed =>
            Nez.Input.IsKeyPressed(Keys.Enter) || Nez.Input.IsKeyPressed(Keys.Space)
            || KingsValley.Engine.Input.UnifiedPointer.IsJustPressed;

        public void Update()
        {
            if (Nez.Input.IsKeyPressed(Keys.Escape))
            {
                GameAudio.StopMusic();
                GameNavigation.GoTo(GameSceneId.MainMenu);
                Entity?.Destroy();
                return;
            }

            _time += Time.DeltaTime;
            bool skip = SkipPressed;

            if (_phase <= 6)
            {
                if (skip || _time >= PageSeconds)
                    Next();
                return;
            }

            // Port card: any key (or the time) leads to the ROM's final menu.
            // end_menu (0xB53A): EF10 == 1 -> sound select (0xB917); otherwise -> puzzle (0xB6B0).
            if (skip || _time >= PageSeconds)
                GameNavigation.GoTo(_pyramid == 1 ? GameSceneId.SoundTest : GameSceneId.Puzzle);
        }

        private void Next()
        {
            _phase++;
            _time = 0f;
            GameAudio.PlayConfirm();
        }

        public override void Render(Batcher batcher, Camera camera)
        {
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);

            if (_phase == 1)
            {
                var (text, _) = StageText(_pyramid);
                string title = $"*** {text} ***";
                MsxScreen.Text(batcher, title, (GameConstants.DesignWidth - title.Length * 8) / 2f, 72, Color.Gold);
                MsxScreen.Text(batcher, "SEAL UNLOCKED", (GameConstants.DesignWidth - 13 * 8) / 2f, 96, Color.White);
                MsxScreen.Text(batcher, "SPACE: NEXT   ESC: MENU", (GameConstants.DesignWidth - 23 * 8) / 2f, 180, Color.Gray);
                return;
            }

            if (_phase >= 2 && _phase <= 6)
            {
                var page = Pages[_phase - 2];
                foreach (var line in page)
                {
                    string upper = line.Text.ToUpperInvariant();
                    MsxScreen.Text(batcher, upper, (GameConstants.DesignWidth - upper.Length * 8) / 2f, line.Y, Color.White);
                }
                MsxScreen.Text(batcher, "SPACE: NEXT   ESC: MENU", (GameConstants.DesignWidth - 23 * 8) / 2f, 180, Color.Gray);
                return;
            }

            MsxScreen.Text(batcher, "CONGRATULATIONS", (GameConstants.DesignWidth - 15 * 8) / 2f, 44, Color.White);
            MsxScreen.Text(batcher, "THE SEAL OF EL GIZA IS OPEN", (GameConstants.DesignWidth - 27 * 8) / 2f, 68, Color.Gold);
            string scoreText = $"SCORE {_run.Score}";
            MsxScreen.Text(batcher, scoreText, (GameConstants.DesignWidth - scoreText.Length * 8) / 2f, 100, Color.White);

            string nextDest = _pyramid == 1 ? "SOUND TEST" : "PUZZLE GAME";
            string prompt = $"ENTERING {nextDest}...";
            MsxScreen.Text(batcher, prompt, (GameConstants.DesignWidth - prompt.Length * 8) / 2f, 134, Color.Gold);
            MsxScreen.Text(batcher, "PRESS SPACE", (GameConstants.DesignWidth - 11 * 8) / 2f, 168, Color.Gray);
            MsxScreen.Text(batcher, "ESC: MENU", (GameConstants.DesignWidth - 9 * 8) / 2f, 188, Color.Gray);
        }
    }
}
