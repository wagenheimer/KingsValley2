using System;
using System.Collections.Generic;
using System.Text;

namespace KingsValley.Core.Extraction;

/// <summary>
/// Reads the 60 stages straight from the cartridge image (RC-760), with no emulator and no dump.
/// </summary>
/// <remarks>
/// <para>Everything here is a table/byte reader on the 128 KB ROM: terrain (2-bit RLE), ladders,
/// golden ladders, items and the 8x8 room grid. The original pipeline read these same tables, but kept
/// the result in JSON files next to the game; reading them at runtime is what lets the free build ship
/// no Konami content.</para>
///
/// <para>Still to port (they were read from openMSX <b>RAM dumps</b> and are not tables): soul stones,
/// exit door, sarcophagi, mechanisms and the player start — the game writes them into RAM while loading
/// the stage, so they need the stage-init routine (a later milestone).</para>
///
/// <para>Banks: the Python extractor read these addresses with the mapper switched
/// (<c>bank 10 → 0x6000</c>, <c>bank 13 → 0xA000</c>); on the 128 KB image that is just an offset.</para>
/// </remarks>
public sealed class RomLevels
{
    /// <summary>ROM offset of bank 10 (mapped at 0x6000 while loading the terrain).</summary>
    private const int Bank10 = 0x14000;

    /// <summary>ROM offset of bank 13 (mapped at 0xA000 while loading items and the grid).</summary>
    private const int Bank13 = 0x1A000;

    private const int RoomWidth = 32;
    private const int RoomHeight = 24;

    private readonly byte[] _rom;

    /// <summary>Creates the reader for a cartridge image (MSX1 128 KB, RC-760).</summary>
    public RomLevels(byte[] rom)
    {
        _rom = rom ?? throw new ArgumentNullException(nameof(rom));
        if (rom.Length < 0x20000)
            throw new ArgumentException("King's Valley II needs the 128 KB cartridge image.", nameof(rom));
    }

    /// <summary>Number of stages in the cartridge.</summary>
    public const int StageCount = 60;

    /// <summary>Reads one stage.</summary>
    /// <param name="stage">1..60.</param>
    public RomLevel Read(int stage)
    {
        if (stage is < 1 or > StageCount)
            throw new ArgumentOutOfRangeException(nameof(stage));

        int[] cells = Terrain(stage);
        (int[][] grid, int roomCount) = RoomGrid(stage);

        var rooms = new List<RomRoom>();
        int count = cells.Length / (RoomWidth * RoomHeight);

        for (int r = 0; r < count; r++)
        {
            var rows = new string[RoomHeight];
            int start = r * RoomWidth * RoomHeight;

            for (int y = 0; y < RoomHeight; y++)
                rows[y] = Encode(cells, start + y * RoomWidth, RoomWidth);

            rooms.Add(new RomRoom(r + 1, rows));
        }

        return new RomLevel
        {
            Stage = stage,
            Pyramid = (stage - 1) / 10 + 1,
            RoomGrid = grid,
            Rooms = rooms,
            Ladders = Ladders(stage, 0x6078),
            GoldenLadders = Ladders(stage, 0x60F0),
            Items = Items(stage),
            Music = ((stage - 1) % 10) / 2,
            RoomCountInGrid = roomCount,
        };
    }

    /// <summary>Terrain cells, one value per 8x8 cell (0 empty, 1 block, 2 ladder, 3 lava).</summary>
    public int[] Terrain(int stage)
    {
        int p = TablePointer(0x6000, stage, Bank10);
        var cells = new List<int>(RoomWidth * RoomHeight * 8);

        while (p < _rom.Length && _rom[p] != 0)
        {
            int run = _rom[p] & 0x3F;
            int value = _rom[p] >> 6;

            for (int i = 0; i < run; i++)
                cells.Add(value);

            p++;
        }

        return cells.ToArray();
    }

    /// <summary>Ladders (table 0x6078) or golden ladders (0x60F0).</summary>
    public List<RomLadder> Ladders(int stage, int table)
    {
        int p = TablePointer(table, stage, Bank10);
        var result = new List<RomLadder>();
        int room = 1;

        while (p + 1 < _rom.Length)
        {
            byte e = _rom[p];

            if (e == 0xFF)
            {
                room++;
                p++;
                continue;
            }

            if (e == 0)
                break;

            byte d = _rom[p + 1];
            int packed = (d << 8) | e;
            int offset = packed >> 7;
            int sub = (e >> 5) & 3;

            result.Add(new RomLadder(room, (offset % 8) * 4 + sub, offset / 8, e & 0x1F));
            p += 2;
        }

        return result;
    }

    /// <summary>Items of the stage (type 1..6, room, position).</summary>
    public List<RomItem> Items(int stage)
    {
        int p = TablePointer(0xAFB1, stage, Bank13);
        var result = new List<RomItem>();

        while (p + 2 < _rom.Length && _rom[p] != 0xFF)
        {
            byte t = _rom[p];
            result.Add(new RomItem(t & 0x0F, t >> 4, _rom[p + 1], _rom[p + 2]));
            p += 3;
        }

        return result;
    }

    /// <summary>8x8 grid with the room number in each used cell (0 = no room).</summary>
    public (int[][] Grid, int RoomCount) RoomGrid(int stage)
    {
        int baseOffset = Bank13 + (0xAB5A - 0xA000) + 8 * (stage - 1);
        var grid = new int[8][];
        int n = 1;

        for (int r = 0; r < 8; r++)
        {
            grid[r] = new int[8];
            byte bits = _rom[baseOffset + r];

            for (int c = 0; c < 8; c++)
            {
                if ((bits & (0x80 >> c)) != 0)
                    grid[r][c] = n++;
            }
        }

        return (grid, n - 1);
    }

    /// <summary>
    /// File offset of the data a table points to. The tables hold CPU addresses of the bank they are
    /// read in (the Python extractor resolved them the same way, with the mapper switched in).
    /// </summary>
    private int TablePointer(int table, int stage, int bank)
    {
        int window = bank == Bank10 ? 0x6000 : 0xA000;
        int tableOffset = bank + (table - window);
        int target = ReadWord(tableOffset + 2 * (stage - 1));
        return bank + (target - window);
    }

    private int ReadWord(int offset) => _rom[offset] | (_rom[offset + 1] << 8);

    private static string Encode(int[] cells, int start, int length)
    {
        var builder = new StringBuilder(length);

        for (int i = 0; i < length; i++)
        {
            int value = start + i < cells.Length ? cells[start + i] : 0;
            builder.Append(value switch { 0 => '.', 1 => '?', 2 => '=', 3 => '#', _ => '.' });
        }

        return builder.ToString();
    }
}

/// <summary>One stage as read from the ROM.</summary>
public sealed class RomLevel
{
    /// <summary>1..60.</summary>
    public int Stage { get; init; }

    /// <summary>Pyramid (1..6): the original groups ten stages per pyramid.</summary>
    public int Pyramid { get; init; }

    /// <summary>8x8 room grid (0 = no room, otherwise the room number in reading order).</summary>
    public int[][] RoomGrid { get; init; } = Array.Empty<int[]>();

    /// <summary>Rooms, each with 24 rows of 32 cells ('.' empty, '?' block, '=' ladder, '#' lava).</summary>
    public List<RomRoom> Rooms { get; init; } = new();

    /// <summary>Ladders.</summary>
    public List<RomLadder> Ladders { get; init; } = new();

    /// <summary>Golden ladders.</summary>
    public List<RomLadder> GoldenLadders { get; init; } = new();

    /// <summary>Items on the floor.</summary>
    public List<RomItem> Items { get; init; } = new();

    /// <summary>Music index (the original changes the theme every two stages).</summary>
    public int Music { get; init; }

    /// <summary>Rooms used according to the grid.</summary>
    public int RoomCountInGrid { get; init; }
}

/// <summary>A room of the stage.</summary>
public sealed record RomRoom(int Index, string[] Rows);

/// <summary>A ladder: room, column, row and length.</summary>
public sealed record RomLadder(int Room, int Column, int Row, int Length);

/// <summary>An item: type (1..6), room and position.</summary>
public sealed record RomItem(int Type, int Room, int Y, int X);
