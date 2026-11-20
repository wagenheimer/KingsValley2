using System;
using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Rooms;

/// <summary>
/// Assembles the room (256x192) as the MSX2 leaves it after <c>44D4</c>: opaque mural, transparent
/// decorations, map (terrain/ladders), golden ladders. Returns a <c>192*256</c> buffer of
/// bytes with the color index (0..15) of each pixel — the original works in indices to animate
/// color 6 (<c>5C14</c>).
///
/// Pure C#: the data (8x8 blocks, mural codes, decorations, terrain) is passed by the caller
/// (the Engine loads it from <c>Content/Rom/Rooms/</c>). Validation is done by comparing the result
/// with the 4bpp bitmap from the emulator's VRAM (<c>tools/kv2-extract/work/room_build.py</c>).
/// </summary>
public static class RoomAssembler
{
    public const int Width = 256;
    public const int Height = 192;
    public const int BlockSize = 8;

    /// <summary>An 8x8 block of the original: 32 bytes, 4bpp (2 pixels per byte).</summary>
    public static byte BlockPixel(byte[] blocks, int code, int x, int y)
    {
        int baseOff = code * 32;
        int row = y & 7;
        int col = x & 7;
        byte b = blocks[baseOff + row * 4 + col / 2];
        return (byte)((col % 2 == 0) ? (b >> 4) : (b & 0x0F));
    }

    /// <summary>
    /// Assembles the room. <paramref name="map"/> is the room terrain (32x24, indices <c>row*32+column</c>).
    /// <paramref name="muralCodes"/> = 32 codes (the 4-row x 8-column strip, repeated 4x6).
    /// <paramref name="decorations"/> = list of stage decorations (already filtered by room or not;
    /// the method filters by <paramref name="room"/>).
    /// </summary>
    public static byte[] Assemble(
        int pyramid,
        int stage,
        int room,
        byte[] blocks,
        IReadOnlyList<int> muralCodes,
        IReadOnlyList<DecorationEntry> decorations,
        TerrainKind[] map,
        IReadOnlyList<LadderDefinition> ladders,
        IReadOnlyList<LadderDefinition> goldenLadders)
    {
        var img = new byte[Width * Height];

        // 1) mural (opaque): 6 strips of 4 rows, 4x8 codes, repeated 4x horizontally and 6x vertically.
        for (int by = 0; by < 6; by++)
            for (int row = 0; row < 4; row++)
                for (int bx = 0; bx < 4; bx++)
                    for (int col = 0; col < 8; col++)
                    {
                        int code = muralCodes[row * 8 + col];
                        int x0 = (bx * 8 + col) * BlockSize;
                        int y0 = (by * 4 + row) * BlockSize;
                        BlitBlock(img, blocks, code, x0, y0, opaque: true);
                    }

        // 2) decorations (transparent).
        foreach (var dec in decorations)
        {
            if (dec.Room != room)
                continue;
            int rows = dec.Rows, cols = dec.Cols;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int code = dec.Blocks[r * cols + c];
                    int x = dec.X + c * BlockSize;
                    int y = dec.Y + r * BlockSize;
                    BlitBlock(img, blocks, code, x, y, opaque: false);
                }
        }

        // 3) map (transparent): 0=nothing, 2=block 5, 3=block 0x5E, 1=ladder (3 in the 1st cell, 4 in the 2nd).
        int prev = 0;
        for (int n = 0; n < GameConstants.RoomColumns * GameConstants.RoomRows; n++)
        {
            int v = (int)map[n];
            if (v == 0)
                continue;
            int col = n % GameConstants.RoomColumns;
            int row = n / GameConstants.RoomColumns;
            int code;
            if (v == 2)
                code = 5;
            else if (v == 3)
                code = 0x5E;
            else
            {
                bool isSecondHalf = col > 0 && (int)map[n - 1] == 1 && prev == 3;
                code = isSecondHalf ? 4 : 3;
            }
            prev = code;
            BlitBlock(img, blocks, code, col * BlockSize, row * BlockSize, opaque: false);
        }

        // 4) golden ladders: repaints the mural (transparent) and draws blocks 1/2 per row.
        foreach (var gl in goldenLadders)
        {
            if (gl.Room != room)
                continue;
            int x = gl.Column * BlockSize;
            int y = gl.Row * BlockSize;
            int height = gl.Length;
            // mural repaint
            for (int r = 0; r < height; r++)
                for (int c = 0; c < 2; c++)
                {
                    int cx = x + c * BlockSize;
                    int cy = y + r * BlockSize;
                    int muralCode = MuralCode(muralCodes, cx, cy);
                    BlitBlock(img, blocks, muralCode, cx, cy, opaque: false);
                }
            // blocks 1 (left) and 2 (right) per row
            for (int r = 0; r < height; r++)
            {
                BlitBlock(img, blocks, 1, x, y + r * BlockSize, opaque: false);
                BlitBlock(img, blocks, 2, x + BlockSize, y + r * BlockSize, opaque: false);
            }
        }

        return img;
    }

    private static int MuralCode(IReadOnlyList<int> muralCodes, int x, int y)
    {
        // 56F8: (y & 0x18) + ((x >> 3) & 7)
        return muralCodes[(y & 0x18) + ((x >> 3) & 7)];
    }

    private static void BlitBlock(byte[] img, byte[] blocks, int code, int x, int y, bool opaque)
    {
        if (x < 0 || y < 0 || x + BlockSize > Width || y + BlockSize > Height)
            return;
        for (int r = 0; r < BlockSize; r++)
            for (int c = 0; c < BlockSize; c++)
            {
                byte p = BlockPixel(blocks, code, c, r);
                if (opaque || p != 0)
                    img[(y + r) * Width + (x + c)] = p;
            }
    }
}

/// <summary>Decoration entry (read from Content/Rom/Rooms/decorations.json).</summary>
public sealed record DecorationEntry(
    int Kind,
    string Shape,
    int Room,
    int Y,
    int X,
    int Rows,
    int Cols,
    int Height,
    int[] Blocks);
