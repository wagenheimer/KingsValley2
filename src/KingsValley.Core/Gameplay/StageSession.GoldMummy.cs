using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// Yellow mummy — type 2 (<c>AE23</c>), ported from the ROM. It leaves the sarcophagus like the white one, but:
/// at the end of each pause it turns <b>towards the player</b> (AFAA) and, while walking, <b>enters the ladder</b> if the
/// player is at another height (B23F + B150/B143). On the ladder: 0x280 (2.5 px/tick), frames 5/6.
/// </summary>
public sealed partial class StageSession
{
    private const int LadderSpeed = 0x280;   // AEFD

    /// <summary>AE8B: stopped facing front; at the end it turns to the player and walks (stuck on both sides, it vanishes).</summary>
    private void GoldPause(Enemy e)
    {
        if (NothingBelow(e))
        {
            StartEnemyFall(e);
            return;
        }

        e.Frame = EnemyFrames.Front;
        if (--e.Timer > 0)
            return;

        e.Dir = DirectionToPlayer(e.Room, e.X) | 2;   // AFAA
        e.SpeedX = MummyWalkSpeed;
        if (CanMove(e))
        {
            StartWalking(e);
            return;
        }

        e.Dir ^= 1;
        if (CanMove(e))
            StartWalking(e);
        else
            Vanish(e, withSound: true);
    }

    /// <summary>AEB5: walks; with the player at another height and a ladder in the right place, it enters it.</summary>
    private void GoldWalk(Enemy e)
    {
        e.Moving = true;
        e.Frame = (e.Dir == 3 ? 3 : 1) + ((TickCount & 4) != 0 ? 1 : 0);   // AF14
        if (NothingBelow(e))
        {
            SnapEnemy(e);
            StartEnemyFall(e);
            return;
        }

        if (!CanMove(e))
        {
            SnapEnemy(e);
            if (++e.Bumps >= MaxWallBumps)
                Vanish(e, withSound: true);
            else
                PauseEnemy(e);
            return;
        }

        int dir;
        switch (PlayerHeight(e))
        {
            case 1 when LadderPair(e, e.X, e.Y):            // B150: ladder on the body, player above
                dir = 0;
                break;
            case 2 when LadderPair(e, e.X, e.Y + 16):       // B143: ladder under the feet, player below
                dir = 1;
                break;
            default:
                return;
        }

        e.Dir = dir;
        e.SpeedY = LadderSpeed;
        e.SpeedX = 0;
        e.X &= ~7;
        e.Phase = 4;
        e.State = EnemyState.Climbing;
    }

    /// <summary>AF4E: climbs up/down; leaves when the ladder ends (B0F9), reverses if it hits.</summary>
    private void GoldClimb(Enemy e)
    {
        e.Moving = true;
        e.Frame = 5 + ((TickCount & 4) != 0 ? 1 : 0);   // AF52/AF1E
        if (ClimbEnds(e))
        {
            // AF73: snaps, zeroes the fraction and the hits, pauses 0x10.
            SnapEnemy(e);
            e.YFraction = 0;
            e.Bumps = 0;
            PauseEnemy(e);
            return;
        }

        if (!CanMove(e))
            e.Dir ^= 1;   // AF6A
    }

    /// <summary>B0F9: the ladder ends if going down hit something, or if there is no ladder in the 2x2 of the next position.</summary>
    private bool ClimbEnds(Enemy e)
    {
        if (e.Dir == 1 && !CanMove(e))
            return true;

        var (x, y, free) = NextPosition(e);
        if (free)
            return false;

        int row = (y + 7) & ~7;
        return !(EnemyTile(e, x, row) == 1 || EnemyTile(e, x, row + 8) == 1
            || EnemyTile(e, x + 8, row + 8) == 1 || EnemyTile(e, x + 8, row) == 1);
    }

    /// <summary>B150/B143: ladder in both cells (x, y) and (x+8, y).</summary>
    private bool LadderPair(Enemy e, int x, int y) =>
        EnemyTile(e, x, y) == 1 && EnemyTile(e, x + 8, y) == 1;

    /// <summary>B23F: 0 same height (±16), 1 player above, 2 player below (grid rows first).</summary>
    private int PlayerHeight(Enemy e)
    {
        int enemyRow = RoomRow(e.Room), playerRow = RoomRow(CurrentRoom);
        if (enemyRow != playerRow)
            return playerRow < enemyRow ? 1 : 2;

        int d = Player.Y - e.Y;
        if (d >= 0)
            return d < 16 ? 0 : 2;
        return d + 16 >= 0 ? 0 : 1;
    }

    private int RoomRow(int room)
    {
        var grid = Definition.RoomGrid;
        for (int r = 0; r < grid.GetLength(0); r++)
            for (int c = 0; c < grid.GetLength(1); c++)
                if (grid[r, c] == room)
                    return r;
        return 0;
    }
}
