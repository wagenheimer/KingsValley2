#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Sound select — the **real** screen of the original's final menu (module of bank 12, branch <c>0xB917</c>).
///
/// The art was captured from the ROM itself (VRAM dump in the emulator, not through the video): the keyboard is
/// the background (<c>sound_select.bin</c>, MSX2 Screen 5 assembled at runtime by `ScreenBuilder`)
/// and the little hand is a separate sprite (<c>sound_select_cursor.bin</c>). The cursor walks over the **19 keys**
/// at the X positions of table <c>0xB9FA</c> of bank 12 (<c>2C 33 3C 45 … CC</c> — the uneven
/// spacing comes from the black keys); ←/→/↑/↓ (or clicking a key) move with the cursor sound
/// (<c>0x42CB</c>), Space/Enter plays the key, Esc/End goes back to the menu.
///
/// The exact order of the 19 ids has not been decoded yet (it is in the module's note table); for now the
/// list uses the extracted songs + the best-known effects, and the screen shows no names — like the
/// original, which only plays the keys.
/// </summary>
public class SoundTestScene : SceneBase
{
    /// <summary>X of each key (table <c>0xB9FA</c>, bank 12) — it is also the little hand's X.</summary>
    private static readonly int[] KeyX =
    {
        0x2C, 0x33, 0x3C, 0x45, 0x4C, 0x55, 0x5C, 0x6C, 0x73, 0x7C,
        0x85, 0x8C, 0x9C, 0xA3, 0xAC, 0xB5, 0xBC, 0xC5, 0xCC,
    };

    /// <summary>Y of the little hand (measured in the ROM capture).</summary>
    private const int CursorY = 122;

    protected override void OnInitialize()
    {
        GameAudio.Initialize();

        var backgroundPath = RomContent.ScreenPath("sound_select");
        var background = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, backgroundPath);
        var cursorPath = RomContent.ScreenPath("sound_select_cursor");
        var cursor = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, cursorPath);

        AddEntity(new Entity("soundtest")).AddComponent(new SoundTestRenderer(background, cursor));
    }

    public override void OnGfxVariantChanged()
    {
        var renderer = FindEntity("soundtest")?.GetComponent<SoundTestRenderer>();
        renderer?.ReloadGraphics();
    }

    private static bool IsBlackKey(int index) => index is 1 or 3 or 5 or 8 or 10 or 13 or 15 or 17;

    private sealed class SoundTestRenderer : RenderableComponent, IUpdatable
    {
        private Texture2D? _background;
        private Texture2D? _cursor;
        private int ScreenOffsetY => _background is null?0:(GameConstants.DesignHeight-_background.Height)/2;
        private readonly IReadOnlyList<(int Id, string Name, bool Music)> _entries;

        private int _index;
        private int _pressAnim;

        public SoundTestRenderer(Texture2D? background, Texture2D? cursor)
        {
            _background = background;
            _cursor = cursor;
            _entries = GameAudio.SoundTestEntries();
        }

        public void ReloadGraphics()
        {
            _background?.Dispose();
            _cursor?.Dispose();
            var backgroundPath = RomContent.ScreenPath("sound_select");
            _background = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, backgroundPath);
            var cursorPath = RomContent.ScreenPath("sound_select_cursor");
            _cursor = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, cursorPath);
        }

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        public void Update()
        {
            if (_pressAnim > 0)
                _pressAnim--;

            bool nextKey = Nez.Input.IsKeyPressed(Keys.Right) || Nez.Input.IsKeyPressed(Keys.D)
                || Nez.Input.IsKeyPressed(Keys.Down) || Nez.Input.IsKeyPressed(Keys.S);
            bool prevKey = Nez.Input.IsKeyPressed(Keys.Left) || Nez.Input.IsKeyPressed(Keys.A)
                || Nez.Input.IsKeyPressed(Keys.Up) || Nez.Input.IsKeyPressed(Keys.W);

            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                nextKey |= pad.DpadRightPressed || pad.DpadDownPressed || pad.IsLeftStickRightPressed() || pad.IsLeftStickDownPressed();
                prevKey |= pad.DpadLeftPressed || pad.DpadUpPressed || pad.IsLeftStickLeftPressed() || pad.IsLeftStickUpPressed();
            }

            if (nextKey && _index < KeyX.Length - 1)
            {
                _index++;
                GameAudio.PlayCursor();
            }

            if (prevKey && _index > 0)
            {
                _index--;
                GameAudio.PlayCursor();
            }

            // Mouse / touch support on the piano keys (tells the black and white keys apart by height)
            if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
            {
                var pointer = KingsValley.Engine.Input.UnifiedPointer.Position;
                pointer.Y-=ScreenOffsetY;
                if (pointer.Y >= 60 && pointer.Y <= 150)
                {
                    bool clickUpper = pointer.Y < 108;
                    int closest = -1;

                    if (clickUpper)
                    {
                        for (int k = 0; k < KeyX.Length; k++)
                        {
                            if (IsBlackKey(k))
                            {
                                int dist = Math.Abs((int)pointer.X - (KeyX[k] + 4));
                                if (dist <= 6)
                                {
                                    closest = k;
                                    break;
                                }
                            }
                        }
                    }

                    if (closest < 0)
                    {
                        int minDist = 16;
                        for (int k = 0; k < KeyX.Length; k++)
                        {
                            if (!IsBlackKey(k))
                            {
                                int dist = Math.Abs((int)pointer.X - (KeyX[k] + 5));
                                if (dist < minDist)
                                {
                                    minDist = dist;
                                    closest = k;
                                }
                            }
                        }
                    }

                    if (closest >= 0)
                    {
                        _index = closest;
                        PlayCurrent();
                    }
                }
            }

            if (Nez.Input.IsKeyPressed(Keys.Space) || Nez.Input.IsKeyPressed(Keys.Enter))
            {
                PlayCurrent();
            }

            if (Nez.Input.IsKeyPressed(Keys.Escape))
            {
                GameAudio.StopMusic();
                GameNavigation.GoTo(GameSceneId.MainMenu);
            }
        }

        private void PlayCurrent()
        {
            _pressAnim = 10;
            if (_entries.Count == 0) return;
            var e = _entries[_index % _entries.Count];
            GameAudio.PlaySoundTest(e.Id, e.Name, e.Music);
        }

        public override void Render(Batcher batcher, Camera camera)
        {
            int offsetY=ScreenOffsetY;
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);

            if (_background != null)
                batcher.Draw(_background, new Vector2(0,offsetY), Color.White);

            // Visual highlight on the active key while pressed
            if (_index >= 0 && _index < KeyX.Length)
            {
                bool isBlack = IsBlackKey(_index);
                if (_pressAnim > 0)
                {
                    var highlight = isBlack
                        ? new Rectangle(KeyX[_index] - 1, offsetY+66, 8, 40)
                        : new Rectangle(KeyX[_index] - 2, offsetY+66, 12, 62);
                    MsxScreen.Fill(batcher, highlight, Color.Gold * 0.45f);
                }

                if (_cursor != null)
                {
                    int cursorY = offsetY+(RomContent.Flavor==ArtFlavor.Msx1?(isBlack?97:121):(isBlack?98:122));
                    if (_pressAnim > 0)
                        cursorY -= 3; // key press animation

                    batcher.Draw(_cursor, new Vector2(KeyX[_index], cursorY), Color.White);
                }
            }

            // Information for the selected range (at the top, between the title and the piano, without overlapping ROM text)
            if (_entries.Count > 0 && _index < _entries.Count)
            {
                var current = _entries[_index % _entries.Count];
                string typeLabel = current.Music ? "BGM" : "SFX";
                string trackInfo = $"[{_index + 1:D2}/19] {current.Name.ToUpperInvariant()} ({typeLabel})";
                MsxScreen.Text(batcher, trackInfo, (GameConstants.DesignWidth - trackInfo.Length * 8) / 2f, offsetY+36, Color.Gold);
            }

            // Discreet instruction in the footer (without covering the ROM's native MSX text)
            string hint = "ARROWS / CLICK: SELECT KEY";
            MsxScreen.Text(batcher, hint, (GameConstants.DesignWidth - hint.Length * 8) / 2f,
                offsetY>0?GameConstants.DesignHeight-8:196, Color.White);
        }
    }
}
