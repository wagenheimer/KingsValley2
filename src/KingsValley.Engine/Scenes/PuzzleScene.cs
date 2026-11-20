#nullable enable
using System;
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
/// **Puzzle game** of the final menu — page B of <c>end_menu</c> (<c>0xB6B0</c>, when <c>EF10 != 1</c>),
/// bank 12. The screen captured from the ROM is <c>PUZZLE GAME</c>: sliding board **5×5** (1..24 + one
/// hole), footer with the original ROM text <c>PUSH SPACE KEY</c> and <c>END ESC KEY</c>.
///
/// <para><b>Rules measured in the ROM</b> (substates of <c>0xB6B0</c> to <c>0xB8F0</c>):</para>
/// <list type="bullet">
/// <item>the board is <b>copied from a fixed table</b> — <c>LD HL,0xB5E0; A = E203 &amp; 3;
/// CALL 0x4D4C</c> (the helper is <c>HL += A*2; HL = (HL)</c>) → one of the **4 shuffled boards** in
/// <c>0xB5E8</c>/<c>0xB601</c>/<c>0xB61A</c>/<c>0xB633</c>, copied 25 bytes into <c>EF20</c>.
/// It does <b>not</b> start solved;</item>
/// <item>the array is **column-major**: index = <c>column*5 + row</c> (<c>0xB800</c>:
/// <c>A = col*4 + col + row</c>);</item>
/// <item>cursor in <c>IX+2</c> (column) and <c>IX+3</c> (row), both 0..4;</item>
/// <item><b>Space / Enter</b> (<c>0xB78A</c>): reads the piece under the cursor; if it is not the hole, <c>0xB824</c>
/// looks for an **adjacent hole** (left/right/up/down, in that order). The piece slides into it and
/// <b>the cursor follows the pushed piece</b> (<c>0xB7B2/0xB7C1/0xB7DB/0xB7E6</c>);</item>
/// <item><b>victory</b> (<c>0xB85E</c>): the board must end up <c>1,2,…,24</c> (hole in the last
/// cell — column 4, row 4) → the ROM adds <b>+3 lives</b> (<c>E240 += 3</c>, BCD, cap <c>0x99</c>) and
/// shows the victory message <c>0xB5C7</c> (<c>ALL RIGHT / REST  3 UP</c>);</item>
/// <item><b>Esc</b> (<c>0xB89D</c>): goes back to the main menu.</item>
/// </list>
/// </summary>
public class PuzzleScene : SceneBase
{
    /// <summary>Lives gained on solving (<c>0xB880</c>: <c>E240 += 3</c>, cap <c>0x99</c>).</summary>
    private const int RewardLives = 3;

    /// <summary>Initial board: index = column*5 + row, 0 = hole (table <c>0xB5E0</c>).</summary>
    private static readonly int[][] Boards =
    [
        // tabuleiro 0 (0xB5E8)
        [11, 17, 23, 4, 18, 10, 1, 12, 13, 19, 7, 22, 9, 24, 14, 21, 8, 20, 3, 0, 6, 2, 16, 5, 15],
        // tabuleiro 1 (0xB601)
        [4, 18, 15, 11, 3, 8, 24, 23, 7, 19, 16, 5, 12, 0, 14, 21, 22, 17, 6, 10, 1, 9, 13, 20, 2],
        // tabuleiro 2 (0xB61A)
        [7, 20, 11, 16, 24, 18, 1, 6, 12, 5, 8, 21, 10, 17, 13, 14, 22, 2, 9, 0, 19, 15, 23, 3, 4],
        // tabuleiro 3 (0xB633)
        [15, 4, 8, 2, 11, 0, 13, 22, 14, 19, 6, 12, 7, 18, 21, 20, 23, 17, 24, 10, 5, 16, 3, 9, 1],
    ];

    protected override void OnInitialize()
    {
        GameAudio.Initialize();

        var bgPath = RomContent.ScreenPath("puzzle");
        var bg = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, bgPath);
        var tilesPath = RomContent.ScreenPath("puzzle_tiles");
        var tiles = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, tilesPath);

        int board = int.TryParse(System.Environment.GetEnvironmentVariable("KINGSVALLEY_PUZZLE_BOARD"),
            out var b) ? Math.Clamp(b, 0, Boards.Length - 1) : 0;

        AddEntity(new Entity("puzzle")).AddComponent(new PuzzleRenderer(bg, tiles, board));
    }

    public override void OnGfxVariantChanged()
    {
        var renderer = FindEntity("puzzle")?.GetComponent<PuzzleRenderer>();
        renderer?.ReloadGraphics();
    }

    private sealed class PuzzleRenderer : RenderableComponent, IUpdatable
    {
        /// <summary>
        /// Exact frame coordinates on the MSX2 Screen 5:
        /// The frame goes from X=88 to 167 (80px = 5 * 16px) and Y=48 to 127 (80px = 5 * 16px).
        /// Each cell is exactly 16×16 pixels and each piece is 16×16 pixels.
        /// </summary>
        private const int BoardX = 88, BoardY = 48, TileSize = 16, Pitch = 16, Size = 5;

        private const int HolePiece = 24;

        private Texture2D? _background;
        private Texture2D? _tiles;

        public void ReloadGraphics()
        {
            _background?.Dispose();
            _tiles?.Dispose();
            var bgPath = RomContent.ScreenPath("puzzle");
            _background = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, bgPath);
            var tilesPath = RomContent.ScreenPath("puzzle_tiles");
            _tiles = ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice, tilesPath);
        }

        /// <summary>Screen cell (row*5+column) → piece (0..24, 24 = hole).</summary>
        private readonly int[] _board = new int[25];

        private int _column, _row;
        private bool _solved;
        private int _solvedTicks;

        private struct SlidingPiece
        {
            public int Piece;
            public int FromCol, FromRow;
            public int ToCol, ToRow;
        }

        private readonly List<SlidingPiece> _slidingPieces = new();
        private float _slideProgress = 1f;
        private const float SlideDuration = 0.09f; // ~5-6 ticks at 60fps: smooth and agile animation

        public PuzzleRenderer(Texture2D? background, Texture2D? tiles, int board)
        {
            _background = background;
            _tiles = tiles;

            LoadBoard(board);

            // Diagnostic: enters the solved state to prove the victory path.
            if (System.Environment.GetEnvironmentVariable("KINGSVALLEY_PUZZLE_SOLVED") == "1")
            {
                SetSolvedBoard();
                CheckSolved();
            }
        }

        public override RectangleF Bounds => new(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight);

        private int CellIndex(int col, int row) => row * Size + col;

        public void Update()
        {
            if (_slideProgress < 1f)
            {
                _slideProgress += Time.DeltaTime / SlideDuration;
                if (_slideProgress >= 1f)
                {
                    _slideProgress = 1f;
                    _slidingPieces.Clear();
                    CheckSolved();
                }
            }

            if (_solved)
            {
                _solvedTicks++;
                if (Nez.Input.IsKeyPressed(Keys.Escape))
                    GameNavigation.GoTo(GameSceneId.MainMenu);
                return;
            }

            // Keys 1..4 to switch between the 4 original ROM boards
            for (int b = 0; b < 4; b++)
            {
                if (Nez.Input.IsKeyPressed(Keys.D1 + b) || Nez.Input.IsKeyPressed(Keys.NumPad1 + b))
                {
                    FinishSlideInstant();
                    LoadBoard(b);
                    GameAudio.PlayConfirm();
                    return;
                }
            }

            // Advanced, intuitive mouse / touch support
            if (KingsValley.Engine.Input.UnifiedPointer.IsJustPressed)
            {
                var pointer = KingsValley.Engine.Input.UnifiedPointer.Position;
                int clickCol = ((int)pointer.X - BoardX) / Pitch;
                int clickRow = ((int)pointer.Y - BoardY) / Pitch;
                if (clickCol >= 0 && clickCol < Size && clickRow >= 0 && clickRow < Size)
                {
                    HandleCellClick(clickCol, clickRow);
                }
            }

            bool left = Nez.Input.IsKeyPressed(Keys.Left) || Nez.Input.IsKeyPressed(Keys.A);
            bool right = Nez.Input.IsKeyPressed(Keys.Right) || Nez.Input.IsKeyPressed(Keys.D);
            bool up = Nez.Input.IsKeyPressed(Keys.Up) || Nez.Input.IsKeyPressed(Keys.W);
            bool down = Nez.Input.IsKeyPressed(Keys.Down) || Nez.Input.IsKeyPressed(Keys.S);

            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                left |= pad.DpadLeftPressed || pad.IsLeftStickLeftPressed();
                right |= pad.DpadRightPressed || pad.IsLeftStickRightPressed();
                up |= pad.DpadUpPressed || pad.IsLeftStickUpPressed();
                down |= pad.DpadDownPressed || pad.IsLeftStickDownPressed();
            }

            // 0xB76B/0xB775/0xB75F/0xB780: mover cursor 0..4 + som de cursor 0x42CB
            bool moved = false;
            if (left && _column > 0) { _column--; moved = true; }
            if (right && _column < Size - 1) { _column++; moved = true; }
            if (up && _row > 0) { _row--; moved = true; }
            if (down && _row < Size - 1) { _row++; moved = true; }
            if (moved)
                GameAudio.PlayCursor();

            // 0xB78A: Space or Enter pushes the piece under the cursor
            if (Nez.Input.IsKeyPressed(Keys.Space) || Nez.Input.IsKeyPressed(Keys.Enter))
            {
                PushAtCursor();
            }

            // Also supports the GamePad A / Start button to push
            for (int i = 0; i < Nez.Input.GamePads.Length; i++)
            {
                var pad = Nez.Input.GamePads[i];
                if (!pad.IsConnected()) continue;
                if (pad.IsButtonPressed(Buttons.A) || pad.IsButtonPressed(Buttons.Start))
                {
                    PushAtCursor();
                    break;
                }
            }

            if (Nez.Input.IsKeyPressed(Keys.Escape))
                GameNavigation.GoTo(GameSceneId.MainMenu);
        }

        private void FinishSlideInstant()
        {
            if (_slideProgress < 1f)
            {
                _slideProgress = 1f;
                _slidingPieces.Clear();
            }
        }

        private void LoadBoard(int board)
        {
            var rom = Boards[Math.Clamp(board, 0, Boards.Length - 1)];
            for (int row = 0; row < Size; row++)
                for (int col = 0; col < Size; col++)
                {
                    int piece = rom[col * Size + row];
                    _board[row * Size + col] = piece == 0 ? HolePiece : piece - 1;
                }
            _column = 0;
            _row = 0;
            _solved = false;
            _solvedTicks = 0;
            _slidingPieces.Clear();
            _slideProgress = 1f;
        }

        private void SetSolvedBoard()
        {
            for (int row = 0; row < Size; row++)
                for (int col = 0; col < Size; col++)
                {
                    int piece = col * Size + row;
                    _board[row * Size + col] = piece;
                }
        }

        /// <summary>
        /// Click handling by mouse or touch. If the click is on the same row or column as the hole,
        /// it slides the pieces toward the hole (supports intuitive block sliding).
        /// </summary>
        private void HandleCellClick(int clickCol, int clickRow)
        {
            FinishSlideInstant();

            int hole = Array.IndexOf(_board, HolePiece);
            int holeCol = hole % Size, holeRow = hole / Size;

            // If the hole was clicked, only move the cursor
            if (clickCol == holeCol && clickRow == holeRow)
            {
                _column = clickCol;
                _row = clickRow;
                GameAudio.PlayCursor();
                return;
            }

            // Clicked on the same column as the hole
            if (clickCol == holeCol)
            {
                _slidingPieces.Clear();
                int step = clickRow < holeRow ? 1 : -1;
                for (int r = holeRow; r != clickRow; r -= step)
                {
                    int fromR = r - step;
                    int fromIdx = CellIndex(clickCol, fromR);
                    int toIdx = CellIndex(clickCol, r);
                    int piece = _board[fromIdx];
                    _board[toIdx] = piece;
                    _slidingPieces.Add(new SlidingPiece
                    {
                        Piece = piece,
                        FromCol = clickCol, FromRow = fromR,
                        ToCol = clickCol, ToRow = r
                    });
                }
                _board[CellIndex(clickCol, clickRow)] = HolePiece;
                _column = clickCol;
                _row = clickRow;
                _slideProgress = 0f;
                GameAudio.PlayCursor();
                return;
            }

            // Clicked on the same row as the hole
            if (clickRow == holeRow)
            {
                _slidingPieces.Clear();
                int step = clickCol < holeCol ? 1 : -1;
                for (int c = holeCol; c != clickCol; c -= step)
                {
                    int fromC = c - step;
                    int fromIdx = CellIndex(fromC, clickRow);
                    int toIdx = CellIndex(c, clickRow);
                    int piece = _board[fromIdx];
                    _board[toIdx] = piece;
                    _slidingPieces.Add(new SlidingPiece
                    {
                        Piece = piece,
                        FromCol = fromC, FromRow = clickRow,
                        ToCol = c, ToRow = clickRow
                    });
                }
                _board[CellIndex(clickCol, clickRow)] = HolePiece;
                _column = clickCol;
                _row = clickRow;
                _slideProgress = 0f;
                GameAudio.PlayCursor();
                return;
            }

            // Not aligned with the hole: only move the cursor
            _column = clickCol;
            _row = clickRow;
            GameAudio.PlayCursor();
        }

        /// <summary>
        /// <c>0xB78A</c> + <c>0xB824</c>: the piece under the cursor slides into an **adjacent** hole
        /// (ROM order: left, right, up, down).
        /// As in the ROM (<c>0xB7B2/0xB7C1/0xB7DB/0xB7E6</c>), <b>the cursor follows the pushed piece</b>.
        /// </summary>
        private void PushAtCursor()
        {
            FinishSlideInstant();

            int index = CellIndex(_column, _row);
            if (_board[index] == HolePiece)
                return;                     // the cursor is on the hole: nothing to push

            int hole = Array.IndexOf(_board, HolePiece);
            int holeCol = hole % Size, holeRow = hole / Size;

            // 0xB824 tests, in this order: column-1, column+1, row-1, row+1
            int dc = 0, dr = 0;
            if (_column > 0 && holeCol == _column - 1 && holeRow == _row) dc = -1;
            else if (_column < Size - 1 && holeCol == _column + 1 && holeRow == _row) dc = 1;
            else if (_row > 0 && holeRow == _row - 1 && holeCol == _column) dr = -1;
            else if (_row < Size - 1 && holeRow == _row + 1 && holeCol == _column) dr = 1;
            else
                return;                     // no adjacent hole

            int piece = _board[index];
            int targetCol = _column + dc;
            int targetRow = _row + dr;
            int target = CellIndex(targetCol, targetRow);

            _board[target] = piece;
            _board[index] = HolePiece;

            _slidingPieces.Clear();
            _slidingPieces.Add(new SlidingPiece
            {
                Piece = piece,
                FromCol = _column, FromRow = _row,
                ToCol = targetCol, ToRow = targetRow
            });
            _slideProgress = 0f;

            // Crucial in the ROM: the cursor follows the pushed piece!
            _column = targetCol;
            _row = targetRow;

            GameAudio.PlayCursor();
        }

        /// <summary><c>0xB85E</c>: 1,2,…,24 with the hole at the end → +3 lives.</summary>
        private void CheckSolved()
        {
            for (int row = 0; row < Size; row++)
                for (int col = 0; col < Size; col++)
                    if (_board[row * Size + col] != col * Size + row)
                        return;

            _solved = true;
            _solvedTicks = 0;
            GameAudio.PlayConfirm();       // victory fanfare
        }

        public override void Render(Batcher batcher, Camera camera)
        {
            MsxScreen.Fill(batcher, new Rectangle(0, 0, GameConstants.DesignWidth, GameConstants.DesignHeight), Color.Black);

            if (_background != null)
                batcher.Draw(_background, Vector2.Zero, Color.White);

            // Guarantees the board background is fully clean/black
            MsxScreen.Fill(batcher, new Rectangle(BoardX, BoardY, Size * Pitch, Size * Pitch), Color.Black);

            if (_tiles != null)
            {
                // Determines which pieces are in the sliding animation
                Span<bool> isSliding = stackalloc bool[25];
                if (_slideProgress < 1f)
                {
                    for (int i = 0; i < _slidingPieces.Count; i++)
                    {
                        var sp = _slidingPieces[i];
                        int idx = CellIndex(sp.ToCol, sp.ToRow);
                        isSliding[idx] = true;
                    }
                }

                // Draws static pieces (each piece is 16x16)
                for (int cell = 0; cell < _board.Length; cell++)
                {
                    if (isSliding[cell])
                        continue;

                    int piece = _board[cell];
                    if (piece == HolePiece)
                        continue;                // the hole shows the black background

                    int srcX = (piece % Size) * TileSize;
                    int srcY = (piece / Size) * TileSize;
                    var source = new Rectangle(srcX, srcY, TileSize, TileSize);
                    var target = new Rectangle(BoardX + (cell % Size) * Pitch, BoardY + (cell / Size) * Pitch, TileSize, TileSize);
                    batcher.Draw(_tiles, target, source, Color.White);
                }

                // Draws pieces in animated sliding
                if (_slideProgress < 1f)
                {
                    // Smooth Ease-Out interpolation
                    float t = Math.Min(1f, _slideProgress);
                    float ease = t * (2f - t);

                    for (int i = 0; i < _slidingPieces.Count; i++)
                    {
                        var sp = _slidingPieces[i];
                        int piece = sp.Piece;
                        int srcX = (piece % Size) * TileSize;
                        int srcY = (piece / Size) * TileSize;
                        var source = new Rectangle(srcX, srcY, TileSize, TileSize);

                        float x = MathHelper.Lerp(BoardX + sp.FromCol * Pitch, BoardX + sp.ToCol * Pitch, ease);
                        float y = MathHelper.Lerp(BoardY + sp.FromRow * Pitch, BoardY + sp.ToRow * Pitch, ease);

                        batcher.Draw(_tiles, new Vector2(x, y), source, Color.White);
                    }
                }
            }

            // 0xB8CF: the ROM cursor on the selected cell (16x16 pixels perfectly aligned)
            var cursor = new Rectangle(BoardX + _column * Pitch, BoardY + _row * Pitch, Pitch, Pitch);
            var color = _solved ? Color.Gold : Color.White;
            MsxScreen.Fill(batcher, new Rectangle(cursor.X, cursor.Y, cursor.Width, 1), color);
            MsxScreen.Fill(batcher, new Rectangle(cursor.X, cursor.Bottom - 1, cursor.Width, 1), color);
            MsxScreen.Fill(batcher, new Rectangle(cursor.X, cursor.Y, 1, cursor.Height), color);
            MsxScreen.Fill(batcher, new Rectangle(cursor.Right - 1, cursor.Y, 1, cursor.Height), color);

            if (_solved)
            {
                // As in ROM 0xB5C7 / 0xB880: replaces the footer texts with "ALL RIGHT" and "REST  3 UP"
                MsxScreen.Fill(batcher, new Rectangle(50, 142, 156, 32), Color.Black);

                string t1 = "ALL RIGHT";
                string t2 = $"REST  {RewardLives} UP";
                string t3 = "ESC: EXIT TO MENU";
                MsxScreen.Text(batcher, t1, (GameConstants.DesignWidth - t1.Length * 8) / 2f, 145, Color.Gold);
                MsxScreen.Text(batcher, t2, (GameConstants.DesignWidth - t2.Length * 8) / 2f, 161, Color.Gold);
                MsxScreen.Text(batcher, t3, (GameConstants.DesignWidth - t3.Length * 8) / 2f, 185, Color.White);
            }
            else
            {
                // Subtle, clean hint in the footer (only characters supported by the MSX font)
                string hint = "BOARD 1 2 3 4";
                MsxScreen.Text(batcher, hint, (GameConstants.DesignWidth - hint.Length * 8) / 2f, 184, new Color(160, 160, 160));
            }
        }
    }
}
