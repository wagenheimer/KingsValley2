#nullable enable
using System;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.SaveSystem;
using KingsValley.Core.Stages;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Input;
using KingsValley.Engine.Navigation;
using Wagenheimer.MonoGameHelper.Common;
using Wagenheimer.MonoGameHelper.Input;
using Wagenheimer.MonoGameHelper.Layout;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Original menus (MSX2 screens extracted from the longplay): title (GAME/EDIT) → START SELECT
/// (NORMAL / PASSWORD / STAGE LOAD) → 8-letter password entry in the game font.
/// </summary>
public class MainMenuScene : SceneBase
{
    protected override void OnInitialize()
    {
        GameAudio.Initialize();
        GameAudio.PlayTitle();
        AddEntity(new Entity("menu")).AddComponent(new MenuRenderer(this));
    }

    public override void OnGfxVariantChanged()
    {
        var renderer = FindEntity("menu")?.GetComponent<MenuRenderer>();
        renderer?.ReloadGraphics();
    }

    private Texture2D? LoadScreen(string name)
    {
        var path = RomContent.ScreenPath(name);
        return ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, path);
    }

    private sealed class MenuRenderer : RenderableComponent, IUpdatable
    {
        private enum Page { Title, StartSelect, Password }

        private static readonly int[] OptionY = { 82, 98, 114 };
        private const int TitleCursorY = 140; // pen next to GAME; EDIT is 16 px below
        private readonly MainMenuScene _scene;
        private readonly StringBuilder _password = new();
        private Texture2D? _title;
        private Texture2D? _select;
        private Page _page = Page.Title;
        private int _option;
        private int _titleOption;
        private string _message = string.Empty;

        /// <summary>Diagnostic: <c>KINGSVALLEY_MENU_PAGE=title|select|password</c> opens directly on the page.</summary>
        private static Page DebugPage
        {
            get
            {
                var v = System.Environment.GetEnvironmentVariable("KINGSVALLEY_MENU_PAGE");
                return v?.Trim().ToLowerInvariant() switch
                {
                    "select" => Page.StartSelect,
                    "password" => Page.Password,
                    _ => Page.Title,
                };
            }
        }

        public MenuRenderer(MainMenuScene scene) => _scene = scene;

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        public override void OnAddedToEntity()
        {
            base.OnAddedToEntity();
            RenderLayer = RenderLayers.Tile;
            _title = _scene.LoadScreen("title");
            _select = _scene.LoadScreen("start_select");
            _page = DebugPage;
        }

        public void ReloadGraphics()
        {
            _title?.Dispose();
            _select?.Dispose();
            _title = _scene.LoadScreen("title");
            _select = _scene.LoadScreen("start_select");
        }

        private bool _pointerMoved;
        private int _idleTicks;

        private static readonly int[] DemoStages = [1, 3, 19, 27, 41, 49, 54];
        private static int _demoIndex = 0;

        private static int NextDemoStage()
        {
            int stage = DemoStages[_demoIndex];
            _demoIndex++;
            if (_demoIndex >= DemoStages.Length)
                _demoIndex = 1; // Konami ROM resets to index 1 (Stage 3), skipping Stage 1 on cycle loop
            return stage;
        }

        private void CheckPointerMoved()
        {
            _pointerMoved = UnifiedPointer.HasMoved;
        }

        public void Update()
        {
            if (UI.SystemMenu.IsOpen)
                return;

            CheckPointerMoved();

            bool anyInput = Confirm() || Cancel() || _pointerMoved
                || Nez.Input.IsKeyPressed(Keys.Up) || Nez.Input.IsKeyPressed(Keys.Down)
                || Nez.Input.IsKeyPressed(Keys.Left) || Nez.Input.IsKeyPressed(Keys.Right);

            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                if (pad.DpadLeftDown || pad.DpadRightDown || pad.DpadUpDown || pad.DpadDownDown)
                    anyInput = true;
            }

            if (_page == Page.Title)
            {
                if (anyInput)
                {
                    _idleTicks = 0;
                }
                else
                {
                    _idleTicks++;
                    // ~12 seconds at 60 Hz = 720 frames
                    if (_idleTicks >= 720)
                    {
                        _idleTicks = 0;
                        GameNavigation.GoToDemo(NextDemoStage());
                        return;
                    }
                }
            }
            else
            {
                _idleTicks = 0;
            }

            if (Cancel())
            {
                if (_page == Page.Title)
                {
                    // ESC is handled by SystemMenu to open the System Menu.
                    return;
                }
                _page = _page == Page.Password ? Page.StartSelect : Page.Title;
                return;
            }

            switch (_page)
            {
                case Page.Title:
                    UpdateTitle();
                    break;
                case Page.StartSelect:
                    UpdateSelect();
                    break;
                case Page.Password:
                    UpdatePassword();
                    break;
            }
        }

        private static bool Confirm()
        {
            if (Nez.Input.IsKeyPressed(Keys.Space) || Nez.Input.IsKeyPressed(Keys.Enter))
                return true;

            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                if (pad.IsButtonPressed(Buttons.A) || pad.IsButtonPressed(Buttons.Start))
                    return true;
            }

            return false;
        }

        private static bool Cancel()
        {
            if (Nez.Input.IsKeyPressed(Keys.Escape))
                return true;

            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                if (pad.IsButtonPressed(Buttons.B) || pad.IsButtonPressed(Buttons.Back))
                    return true;
            }

            return false;
        }

        /// <summary>↑/↓ move the cursor (keyboard or the gamepad's d-pad/stick); returns true if it moved.</summary>
        private static int MoveCursor(int current, int count)
        {
            int next = current;
            bool down = Nez.Input.IsKeyPressed(Keys.Down) || Nez.Input.IsKeyPressed(Keys.S);
            bool up = Nez.Input.IsKeyPressed(Keys.Up) || Nez.Input.IsKeyPressed(Keys.W);

            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                down |= pad.DpadDownPressed || pad.IsLeftStickDownPressed();
                up |= pad.DpadUpPressed || pad.IsLeftStickUpPressed();
            }

            if (down)
                next = Math.Min(current + 1, count - 1);
            if (up)
                next = Math.Max(current - 1, 0);
            if (next != current)
                GameAudio.PlayCursor();
            return next;
        }

        private void ConfirmTitle()
        {
            GameAudio.PlayConfirm();
            if (_titleOption == 1)
            {
                GameNavigation.GoTo(GameSceneId.Editor);
                return;
            }

            // MSX1: the title screen already has the GAME/EDIT box (there is no separate selection page
            // from the MSX2) — GAME starts the run directly.
            if (RomContent.Flavor == ArtFlavor.Msx1)
            {
                _message = string.Empty;
                Start(GameRun.New(ProfileSaveService.Load().HiScore));
                return;
            }

            _message = string.Empty;
            _page = Page.StartSelect;
            GameAudio.PlayMenu();
        }

        private void UpdateTitle()
        {
            // The title jingle plays once; then the menu music comes in.
            if (!GameAudio.IsMusicPlaying)
                GameAudio.PlayMenu();

            var pointerPos = UnifiedPointer.Position;
            bool clicked = UnifiedPointer.IsJustPressed;

            // Mouse hover and click on the Title options
            int baseCursorY = RomContent.Flavor == ArtFlavor.Msx1 ? 10 + 137 : TitleCursorY;
            for (int i = 0; i < 2; i++)
            {
                var rect = new Rectangle(50, baseCursorY + i * 16 - 4, 100, 16);
                if (rect.Contains(pointerPos.X, pointerPos.Y))
                {
                    if (_pointerMoved && _titleOption != i)
                    {
                        _titleOption = i;
                        GameAudio.PlayCursor();
                    }
                    if (clicked)
                    {
                        _titleOption = i;
                        ConfirmTitle();
                        return;
                    }
                }
            }

            _titleOption = MoveCursor(_titleOption, 2);
            if (Confirm())
                ConfirmTitle();
        }

        private void ConfirmSelect()
        {
            GameAudio.PlayConfirm();
            _message = string.Empty;
            switch (_option)
            {
                case 0:
                    Start(GameRun.New(ProfileSaveService.Load().HiScore));
                    break;
                case 1:
                    _page = Page.Password;
                    _password.Clear();
                    break;
                default:
                    // Looks for the first saved custom stage in slots 1 to 10
                    string? savedPath = null;
                    for (int s = 1; s <= 10; s++)
                    {
                        var p = System.IO.Path.Combine(RomContent.CustomLevelsDirectory, $"custom_stage_{s:D2}.json");
                        if (System.IO.File.Exists(p))
                        {
                            savedPath = p;
                            break;
                        }
                    }

                    // Fallback to the legacy file if it exists
                    if (savedPath == null)
                    {
                        var oldPath = System.IO.Path.Combine(RomContent.LevelsDirectory, "editor_stage_01.json");
                        if (System.IO.File.Exists(oldPath))
                            savedPath = oldPath;
                    }

                    if (savedPath != null)
                    {
                        var json = System.IO.File.ReadAllText(savedPath);
                        var stage = StageLoader.Parse(json);
                        GameNavigation.StageOverride = stage;
                        Start(GameRun.New(ProfileSaveService.Load().HiScore, stage.Number > 0 ? stage.Number : 1));
                    }
                    else
                    {
                        // No saved custom stage: goes straight to the Level Editor to create one
                        GameNavigation.GoTo(GameSceneId.Editor);
                    }
                    break;
            }
        }

        private void UpdateSelect()
        {
            var pointerPos = UnifiedPointer.Position;
            bool clicked = UnifiedPointer.IsJustPressed;

            // Mouse hover and click on the Selection options
            for (int i = 0; i < OptionY.Length; i++)
            {
                var rect = new Rectangle(76, OptionY[i] - 4, 120, 16);
                if (rect.Contains(pointerPos.X, pointerPos.Y))
                {
                    if (_pointerMoved && _option != i)
                    {
                        _option = i;
                        GameAudio.PlayCursor();
                    }
                    if (clicked)
                    {
                        _option = i;
                        ConfirmSelect();
                        return;
                    }
                }
            }

            if (UnifiedPointer.IsSecondaryJustPressed || Cancel())
            {
                _page = Page.Title;
                return;
            }

            _option = MoveCursor(_option, OptionY.Length);
            if (Confirm())
                ConfirmSelect();
        }

        private void TrySubmitPassword()
        {
            if (_password.Length != 8)
                return;

            var text = _password.ToString();
            int hi = ProfileSaveService.Load().HiScore;
            if (text == StagePassword.Festival)
            {
                // 6B24: play_clear (stage 1, REST 1) + E255 = 1 (invencibilidade).
                GameAudio.PlayConfirm();
                Start(GameRun.Cheat(hi, invincible: true));
            }
            else if (StagePassword.TryDecode(text, out int stage, out int lives))
            {
                GameAudio.PlayConfirm();
                Start(GameRun.New(hi, stage, lives));
            }
            else if (text == StagePassword.TryAgain)
            {
                // 6B24: play_clear (stage 1, REST 1) + E217 = 1 (continue habilitado).
                GameAudio.PlayConfirm();
                Start(GameRun.Cheat(hi, continueEnabled: true));
            }
            else
            {
                _message = "WRONG PASSWORD";
            }
        }

        private void UpdatePassword()
        {
            if (UnifiedPointer.IsSecondaryJustPressed || Cancel())
            {
                _page = Page.StartSelect;
                return;
            }

            var pointerPos = UnifiedPointer.Position;
            if (UnifiedPointer.IsJustPressed)
            {
                // [BACK] button
                if (new Rectangle(56, 146, 60, 16).Contains(pointerPos.X, pointerPos.Y))
                {
                    GameAudio.PlayCursor();
                    _page = Page.StartSelect;
                    return;
                }
                // [DEL] button
                if (new Rectangle(144, 146, 52, 16).Contains(pointerPos.X, pointerPos.Y))
                {
                    if (_password.Length > 0)
                    {
                        _password.Length--;
                        GameAudio.PlayCursor();
                    }
                    return;
                }
                // [OK] button
                if (_password.Length == 8 && new Rectangle(104, 130, 48, 16).Contains(pointerPos.X, pointerPos.Y))
                {
                    TrySubmitPassword();
                    return;
                }
            }

            foreach (var key in Nez.Input.CurrentKeyboardState.GetPressedKeys())
            {
                if (!Nez.Input.IsKeyPressed(key))
                    continue;
                if (key >= Keys.A && key <= Keys.Z && _password.Length < 8)
                {
                    _password.Append((char)('A' + (key - Keys.A)));
                    GameAudio.PlayCursor();
                }
                else if (key == Keys.Back && _password.Length > 0)
                {
                    _password.Length--;
                }
            }

            if (Nez.Input.IsKeyPressed(Keys.Enter))
                TrySubmitPassword();
        }

        private static void Start(GameRun run) => GameNavigation.GoToIntro(run);

        public override void Render(Batcher batcher, Camera camera)
        {
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);
            if (_page == Page.Title)
            {
                int offsetY = _title != null ? (GameConstants.DesignHeight - _title.Height) / 2 : 0;
                if (_title != null)
                    batcher.Draw(_title, new Vector2(0, offsetY), Color.White);
                else
                    MsxScreen.Text(batcher, "NO ROM DATA", 84, 100);

                if (RomContent.Flavor == ArtFlavor.Msx1)
                {
                    // The authentic MSX1 GAME/EDIT cursor: the horizontal laser pointer (-||=-)
                    // with TMS colors (dark red, light red, cyan, light blue and white).
                    DrawMsx1Cursor(batcher, 56, offsetY + 137 + _titleOption * 16);
                }
                else
                {
                    MsxScreen.Draw(batcher, "title_cursor", 56, TitleCursorY + _titleOption * 16);
                }

                DrawMessage(batcher, offsetY + 184);
                return;
            }

            if (_page == Page.StartSelect)
            {
                int offsetY = _select != null ? (GameConstants.DesignHeight - _select.Height) / 2 : 0;
                if (_select != null)
                    batcher.Draw(_select, new Vector2(0, offsetY), Color.White);
                MsxScreen.Draw(batcher, "arrow", 82, offsetY + OptionY[_option]);
                DrawMessage(batcher, offsetY + 150);
                return;
            }

            MsxScreen.Text(batcher, "INPUT PASSWORD", 72, 88);
            var shown = _password.ToString().PadRight(8, '_');
            MsxScreen.Text(batcher, shown.Replace('_', ' '), 96, 106);
            for (int i = 0; i < 8; i++)
                MsxScreen.Fill(batcher, new Rectangle(96 + i * 8, 114, 7, 1), Color.White);

            if (_password.Length == 8)
                MsxScreen.Text(batcher, "[ OK ]", 104, 130);

            MsxScreen.Text(batcher, "[BACK]", 60, 148);
            MsxScreen.Text(batcher, "[DEL]", 148, 148);

            DrawMessage(batcher, 170);
        }

        /// <summary>Draws the authentic 16x7 laser/dardo cursor of King's Valley 2 on MSX1.</summary>
        private static void DrawMsx1Cursor(Batcher batcher, int x, int y)
        {
            Color darkRed = new(218, 72, 72);
            Color lightRed = new(255, 109, 109);
            Color lightBlue = new(109, 109, 255);
            Color cyan = new(72, 218, 255);
            Color white = Color.White;

            // Row 0
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 0, 1, 1), lightRed);

            // Row 1
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 1, 1, 1), darkRed);

            // Row 2
            MsxScreen.Fill(batcher, new Rectangle(x + 2, y + 2, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 4, y + 2, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 2, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 8, y + 2, 7, 1), lightBlue);

            // Row 3 (center bar + white tip)
            MsxScreen.Fill(batcher, new Rectangle(x + 0, y + 3, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 2, y + 3, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 4, y + 3, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 3, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 8, y + 3, 7, 1), cyan);
            MsxScreen.Fill(batcher, new Rectangle(x + 15, y + 3, 1, 1), white);

            // Row 4
            MsxScreen.Fill(batcher, new Rectangle(x + 2, y + 4, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 4, y + 4, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 4, 1, 1), darkRed);
            MsxScreen.Fill(batcher, new Rectangle(x + 8, y + 4, 7, 1), white);

            // Row 5
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 5, 1, 1), darkRed);

            // Row 6
            MsxScreen.Fill(batcher, new Rectangle(x + 6, y + 6, 1, 1), darkRed);
        }

        private void DrawMessage(Batcher batcher, int y)
        {
            if (_message.Length > 0)
                MsxScreen.Text(batcher, _message, (GameConstants.DesignWidth - _message.Length * MsxScreen.CharSize) / 2f, y);
        }
    }
}
