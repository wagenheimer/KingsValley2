#nullable enable
using System;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Path of the **world map tour** (the ROM's <c>world_tour</c> 0x6FFD), decoded from the tables:
/// <list type="bullet">
/// <item><c>world_xy</c> (0x71CA, banks 1-3): initial (x, y) position per world —
/// <c>43,78 · 59,46 · 99,48 · 102,83 · 83,94</c>;</item>
/// <item><c>0xBA57</c> (bank D): one pointer per world to <c>(step, direction)</c> pairs terminated
/// by <c>0xFF</c>; <c>world_path_scan</c> uses the last pair with <c>step &lt;= EDC8</c>;</item>
/// <item><c>world_len</c> (0x7177): total steps per world — <c>88 · 184 · 136 · 142 · 149</c>;</item>
/// <item><c>world_delta</c> (0x7140): direction <c>0</c> = Y−1 (up), <c>1</c> = Y+1 (down), <c>2</c> = X−1 (left),
/// <c>3</c> = X+1 (right). Each step lasts **8 ticks** (<c>world_step</c> writes
/// <c>E204 = 8</c>).</item>
/// </list>
/// The tour coordinates are in **2 px units** of the map screen (it is the step of <c>world_delta</c>).
/// </summary>
public static class WorldTour
{
    /// <summary>Initial tour position per world (<c>world_xy</c> 0x71CA: byte 0 is Y, byte 1 is X).</summary>
    private static readonly (int X, int Y)[] Start =
    [
        (78, 43), (46, 59), (48, 99), (83, 102), (94, 83),
    ];

    /// <summary>Total steps per world (<c>world_len</c> 0x7177).</summary>
    private static readonly int[] Length = [88, 184, 136, 142, 149];

    /// <summary><c>(step, direction)</c> pairs per world (table 0xBA57 of bank D).</summary>
    private static readonly (int Step, int Dir)[][] Paths =
    [
        // mundo 1 — 17 pares
        [(0,2),(3,1),(6,2),(11,0),(25,2),(31,1),(39,2),(44,1),(47,3),(52,1),(57,3),(60,1),(63,2),(71,1),(76,3),(79,1),(82,2)],
        // mundo 2 — 36 pares
        [(0,2),(3,1),(6,2),(11,1),(16,3),(21,1),(24,3),(29,0),(32,3),(46,1),(54,2),(60,0),(63,2),(68,1),(76,2),(84,0),
         (89,2),(94,1),(97,2),(103,1),(108,3),(111,1),(114,3),(119,0),(122,3),(125,1),(130,3),(135,0),(140,3),(148,0),
         (151,3),(154,1),(160,2),(165,1),(170,2),(181,1)],
        // mundo 3 — 19 pares
        [(0,3),(3,1),(11,2),(29,1),(33,3),(57,0),(63,3),(66,1),(69,3),(74,0),(82,2),(87,0),(95,3),(100,0),(103,3),(106,1),
         (117,3),(125,1),(133,3)],
        // mundo 4 — 13 pares
        [(0,0),(8,3),(16,1),(32,3),(48,0),(62,2),(70,0),(75,3),(86,0),(115,2),(121,1),(126,2),(131,1)],
        // mundo 5 — 20 pares
        [(0,2),(8,1),(11,2),(17,0),(31,3),(34,0),(39,3),(47,0),(55,2),(63,0),(68,3),(87,0),(93,2),(104,1),(107,2),(123,1),
         (131,2),(134,1),(139,2),(141,1)],
    ];

    /// <summary>Total steps of the world (1-based).</summary>
    public static int StepsOf(int world) => Length[Math.Clamp(world, 1, Length.Length) - 1];

    /// <summary>Position in screen pixels at the requested step, applying the ROM deltas.</summary>
    public static (int X, int Y) PositionAt(int world, int step)
    {
        var (x, y, _) = StateAt(world, step);
        return (x, y);
    }

    /// <summary>Position (X, Y) and current direction (0=stopped, 1=Y+1 down, 2=X-1 left, 3=X+1 right, 4=Y-1 up) at the requested step.</summary>
    public static (int X, int Y, int Dir) StateAt(int world, int step)
    {
        int index = Math.Clamp(world, 1, Start.Length) - 1;
        var (x, y) = Start[index];
        var path = Paths[index];
        int dir = 0;

        for (int s = 0; s < step; s++)
        {
            foreach (var pair in path)
            {
                if (pair.Step > s)
                    break;
                dir = pair.Dir;
            }

            switch (dir)
            {
                case 0: y--; break; // ROM 0x7140: DEC (HL) when A == 0 (up)
                case 1: y++; break; // ROM 0x7140: INC (HL) when A == 1 (down)
                case 2: x--; break; // ROM 0x7140: DEC HL when A == 2 (left)
                case 3: x++; break; // ROM 0x7140: INC HL when A == 3 (right)
            }
        }

        foreach (var pair in path)
        {
            if (pair.Step > step)
                break;
            dir = pair.Dir;
        }

        return (x, y, dir);
    }
}
