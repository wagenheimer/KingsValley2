namespace KingsValley.Core.Gameplay;

/// <summary>
/// Room navigation on the stage's 8x8 grid. Leaving through an edge leads to the neighbor; if there is none,
/// it wraps around to the last room of that row/column (this is what makes the stage 1 ladder exit through
/// the ceiling and come back through the floor of the same room).
/// </summary>
public static class RoomGridNavigator
{
    private const int GridSize = 8;

    /// <summary>Neighbor room of <paramref name="room"/> in the direction (dx, dy).</summary>
    public static int Neighbor(int[,] grid, int room, int dx, int dy)
    {
        if (!TryFind(grid, room, out int r, out int c))
            return room;

        for (int step = 1; step <= GridSize; step++)
        {
            int nr = Wrap(r + dy * step), nc = Wrap(c + dx * step);
            if (grid[nr, nc] != 0)
                return grid[nr, nc];
        }

        return room;
    }

    private static int Wrap(int v) => ((v % GridSize) + GridSize) % GridSize;

    private static bool TryFind(int[,] grid, int room, out int row, out int column)
    {
        for (row = 0; row < GridSize; row++)
        {
            for (column = 0; column < GridSize; column++)
            {
                if (grid[row, column] == room)
                    return true;
            }
        }

        row = column = -1;
        return false;
    }
}
