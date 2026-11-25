using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// <b>Mutable</b> terrain of a room (32x24 cells) — the copy the player digs. Ladders are
/// written on top of the terrain, as in the original (the ROM routine swaps the cell's 2 bits for
/// <see cref="TerrainKind.Ladder"/>, which is why a ladder crosses the floor).
/// </summary>
public sealed class RoomMap
{
    private readonly TerrainKind[] _cells;
    private readonly bool[] _rock = new bool[GameConstants.RoomColumns * GameConstants.RoomRows];
    private readonly bool[] _mechanism = new bool[GameConstants.RoomColumns * GameConstants.RoomRows];

    /// <summary>Room number (1-based).</summary>
    public int Index { get; }

    /// <summary>
    /// Terrain version: goes up on every changed cell (digging, drill...). Whoever draws the terrain from
    /// cache (the assembled room texture) uses it to know it must be rebuilt.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Creates the room from the definition + stage ladders.</summary>
    public RoomMap(RoomDefinition room, IEnumerable<LadderDefinition> ladders)
    {
        Index = room.Index;
        _cells = (TerrainKind[])room.Cells.Clone();
        foreach (var ladder in ladders.Where(l => l.Room == room.Index))
        {
            for (int r = ladder.Row; r < ladder.Row + ladder.Length; r++)
            {
                Set(ladder.Column, r, TerrainKind.Ladder);
                Set(ladder.Column + 1, r, TerrainKind.Ladder);
            }
        }
    }

    /// <summary>
    /// Cell for the rules (outside the room = empty). A stopped stone (type 4) writes wall on top
    /// (B9A2: OR 3) — so here it shows up as <see cref="TerrainKind.Wall"/>.
    /// </summary>
    public TerrainKind Get(int column, int row)
    {
        if (column < 0 || column >= GameConstants.RoomColumns || row < 0 || row >= GameConstants.RoomRows)
            return TerrainKind.Empty;

        int i = row * GameConstants.RoomColumns + column;
        return (_rock[i] || _mechanism[i]) ? TerrainKind.Wall : _cells[i];
    }

    /// <summary>Marks/unmarks a cell as occupied by a solid mechanism (rotating door, trap, etc.).</summary>
    public void SetMechanism(int column, int row, bool on)
    {
        if (column >= 0 && column < GameConstants.RoomColumns && row >= 0 && row < GameConstants.RoomRows)
            _mechanism[row * GameConstants.RoomColumns + column] = on;
    }

    public bool IsMechanism(int column, int row)
    {
        if (column < 0 || column >= GameConstants.RoomColumns || row < 0 || row >= GameConstants.RoomRows)
            return false;
        return _mechanism[row * GameConstants.RoomColumns + column];
    }

    /// <summary>Drawn terrain (without the stones, which are sprites).</summary>
    public TerrainKind[] Snapshot() => (TerrainKind[])_cells.Clone();

    public TerrainKind GetTerrain(int column, int row)
    {
        if (column < 0 || column >= GameConstants.RoomColumns || row < 0 || row >= GameConstants.RoomRows)
            return TerrainKind.Empty;

        return _cells[row * GameConstants.RoomColumns + column];
    }

    /// <summary>B9A2/B9F0: marks/unmarks a cell as occupied by a stone.</summary>
    public void SetRock(int column, int row, bool on)
    {
        if (column >= 0 && column < GameConstants.RoomColumns && row >= 0 && row < GameConstants.RoomRows)
            _rock[row * GameConstants.RoomColumns + column] = on;
    }

    /// <summary>Clears all rock occupancy marks in the room.</summary>
    public void ClearRock()
    {
        Array.Clear(_rock, 0, _rock.Length);
    }

    /// <summary>Restores room cells from a saved state and bumps map version.</summary>
    public void RestoreCells(TerrainKind[] cells)
    {
        Array.Copy(cells, _cells, Math.Min(cells.Length, _cells.Length));
        Version++;
    }

    private void Set(int column, int row, TerrainKind kind)
    {
        if (column < 0 || column >= GameConstants.RoomColumns || row < 0 || row >= GameConstants.RoomRows)
            return;

        int i = row * GameConstants.RoomColumns + column;
        if (_cells[i] == kind)
            return;

        _cells[i] = kind;
        Version++;
    }

    /// <summary>Cell at pixel (x, y).</summary>
    public TerrainKind At(int x, int y) => Get(FloorDiv(x), FloorDiv(y));

    /// <summary>Solid pixel?</summary>
    public bool IsSolid(int x, int y) => At(x, y).IsSolid();

    /// <summary>Ladder pixel?</summary>
    public bool IsLadder(int x, int y) => At(x, y) == TerrainKind.Ladder;

    /// <summary>Top of ladder (ladder with no ladder above) — one can stand on it.</summary>
    public bool IsLadderTop(int x, int y) => IsLadder(x, y) && !IsLadder(x, y - GameConstants.CellSize);

    /// <summary>Removes a diggable block. Returns true if it dug.</summary>
    public bool Dig(int column, int row)
    {
        if (Get(column, row) != TerrainKind.Soft)
            return false;

        Set(column, row, TerrainKind.Empty);
        return true;
    }

    /// <summary>Native mechanism stamping changes terrain and invalidates assembled room graphics.</summary>
    internal void StampTerrain(int column,int row,TerrainKind kind) => Set(column,row,kind);

    /// <summary>0xBD65 <c>trap_erase</c>: zeroes the cell (the <c>stamp_rect</c> with A=0 clears the 2 bits).</summary>
    public void Clear(int column, int row)
    {
        Set(column, row, TerrainKind.Empty);
        SetMechanism(column, row, false);
    }

    /// <summary>Division by 8 rounding down (works for negatives).</summary>
    public static int FloorDiv(int pixels) => pixels >> 3;
}
