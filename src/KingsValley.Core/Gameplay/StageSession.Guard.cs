using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// Type 3 — the one that hops (<c>B2AA</c>), ported from the ROM. It appears in a cloud (B2FE), turns into a yellow statue
/// (frames 7/8, colors 0x0A/0x47 that stay forever, B2DB) and then goes hopping after the player;
/// uses the ladder when the player is at another height (1 px/tick, frames 13/14).
/// States: 0 appearing, 1 on the ground (B307), 2 hopping (B4BB), 3 ladder (B5B7), 4 falling (B5FB).
/// </summary>
public sealed partial class StageSession
{
    private static readonly int[] GuardAppearFrames = { 0x18, 0x19, 0x18, 0x19, 0x18, 0x19, 0x07, 0x07 }; // B2FE
    private static readonly int[] HopHigh = { 5, 5, 4, 3, 2, 2, 1, 1, 0, 0, -1, -1, -2, -2, -3, -4, -5 };  // B595
    private static readonly int[] HopLowTable = { 5, 4, 3, 2, 2, 1, 1, 1, 0, 0, -1, -1, -1, -2, -2, -3, -4 }; // B5A6
    private const int GuardHopSpeed = 0xF0;    // B35F
    private const int GuardHopTicks = 0x11;    // B369
    private const int GuardGroundWait = 8;     // B2E8/B536
    private const int GuardLadderSpeed = 0x100; // B447
    private const int GuardMaxReversals = 0x10; // B357

    /// <summary>B274: born as a cloud, facing the player.</summary>
    private static void InitGuard(Enemy e, int towardPlayer)
    {
        e.Dir = towardPlayer | 2;
        e.Frame = 0x18;
        e.HopX = 0xFF;
        e.LastLadder = 0xFF;
        e.Hops = 0x80;
        e.Reversals = 0;
        e.Timer = 0;
        e.Flags = 0;
    }

    private void UpdateGuard(Enemy e)
    {
        switch (e.Phase)
        {
            case 0: GuardAppear(e); break;
            case 1: GuardGround(e); break;
            case 2: GuardHop(e); break;
            case 3: GuardClimb(e); break;
            case 4: GuardFall(e); break;
        }
    }

    /// <summary>B2BA: cloud/statue, 8 ticks per animation step (B6E0); at the end it becomes dangerous.</summary>
    private void GuardAppear(Enemy e)
    {
        int index = ++e.Timer >> 3;
        if (index < GuardAppearFrames.Length)
        {
            int f = GuardAppearFrames[index];
            e.Frame = f == 7 ? 7 + (e.Dir & 1) : f;
            if (f == 7)
                e.Yellow = true;   // B2DB: colors 0x0A/0x47
            return;
        }

        e.Timer = GuardGroundWait;
        e.Flags = 3;
        e.Phase = 1;
        e.State = EnemyState.Pausing;
    }

    /// <summary>B307: on the ground; waits 8 ticks and decides between ladder and hop.</summary>
    private void GuardGround(Enemy e)
    {
        if (!GuardGroundAt(e, 0))
        {
            GuardStartFall(e);
            return;
        }

        e.Frame = 9 + (e.Dir & 1) * 2;   // B30D/B4AF
        if (--e.Timer > 0)
            return;

        if (e.Hops < 2 && (byte)(e.HopX - e.X + 12) < 0x19)
        {
            GuardStartHop(e);
            return;
        }

        switch (PlayerHeight(e))
        {
            case 1:
                GuardTowardLadder(e, up: true);
                return;
            case 2:
                GuardTowardLadder(e, up: false);
                return;
        }

        if (e.Reversals < 2)
        {
            GuardStartHop(e);
            return;
        }

        // B3F5: already reversed twice — tries the ladder (down if the counter is odd); with no aligned
        // ladder it falls into the other direction's logic (odd → B417 up, even → B452 down).
        bool down = (e.Reversals & 1) != 0;
        if (!LadderAligned(e, e.Y + (down ? 16 : 0)))
            GuardEnterLadder(e, down ? 1 : 0);
        else
            GuardTowardLadder(e, up: down);
    }

    /// <summary>B417/B452: ladder up/down if aligned; otherwise it hops.</summary>
    private void GuardTowardLadder(Enemy e, bool up)
    {
        bool justUsed = up ? e.LastLadder == 0 : e.LastLadder == 1;
        if (!justUsed && e.Hops < 2)
        {
            GuardStartHop(e);
            return;
        }

        if (LadderAligned(e, e.Y + (up ? 0 : 16)))
            GuardStartHop(e);
        else
            GuardEnterLadder(e, up ? 0 : 1);
    }

    /// <summary>B42F: enters the ladder (vy 0x100, vx 0).</summary>
    private static void GuardEnterLadder(Enemy e, int dir)
    {
        e.Dir = dir;
        e.LastLadder = dir;
        e.Hops = 0;
        e.Moving = true;
        e.Reversals = 0;
        e.SpeedX = 0;
        e.SpeedY = GuardLadderSpeed;
        e.Phase = 3;
        e.State = EnemyState.Climbing;
    }

    /// <summary>
    /// B471: returns <c>true</c> (= the ladder CANNOT be used) if neither of the 2 cells is a ladder or if it
    /// is too misaligned; otherwise it snaps X to the ladder and returns <c>false</c>.
    /// </summary>
    private bool LadderAligned(Enemy e, int y)
    {
        int c = (EnemyTile(e, e.X, y) == 1 ? 0 : 1) | (EnemyTile(e, e.X + 8, y) == 1 ? 0 : 2);
        int shift;
        switch (c)
        {
            case 0: shift = 0; break;
            case 3: return true;
            case 1:
                if ((e.X & 7) < 1) return true;
                shift = 8;
                break;
            default:
                if ((e.X & 7) >= 7) return true;
                shift = -8;
                break;
        }

        e.X = (e.X + shift) & ~7;
        return false;
    }

    /// <summary>B33C/B60E/B385: chooses the hop direction and the type (high/low) and starts.</summary>
    private void GuardStartHop(Enemy e)
    {
        e.Dir |= 2;
        if (e.HopX == 0xFF)
        {
            int dx = Player.X - e.X + 2;
            e.Dir = dx is >= 0 and < 4 ? ((TickCount & 0x80) != 0 ? 3 : 2) : DirectionToPlayer(e.Room, e.X) | 2;
        }
        else if (e.HopX == e.X)
        {
            e.Dir = (e.Dir ^ 1) | 2;
        }

        if (!HopSpace(e))
        {
            e.Dir = (e.Dir ^ 1) | 2;
            if (!HopSpace(e) || e.Reversals >= GuardMaxReversals)
            {
                Vanish(e, withSound: true);
                return;
            }

            e.Reversals++;
        }

        e.SpeedX = GuardHopSpeed;
        e.Moving = false;
        e.Timer = GuardHopTicks;
        e.HopX = e.X;
        e.SavedDir = e.Dir;
        e.Hops++;
        PlaySound(OriginalSounds.GuardHop, e.Room);
        e.Phase = 2;
        e.State = EnemyState.Airborne;
    }

    /// <summary>B385: can hop (no wall on the side at y-8 AND at y); low hop if the head hits.</summary>
    private bool HopSpace(Enemy e)
    {
        if (e.Y < 8)
        {
            e.HopLow = true;
            return true;
        }

        if (SideSolid(e, e.Y - 8) && SideSolid(e, e.Y))
            return false;

        int x = (e.Dir & 1) != 0 ? Math.Min(e.X + 8, 0xFF) : Math.Max(e.X - 8, 0);
        e.HopLow = false;
        for (int i = 0; i < 3; i++)
        {
            if (EnemyTile(e, x + 8 * i, e.Y - 8) >= 2)
            {
                e.HopLow = true;
                break;
            }
        }

        return true;
    }

    /// <summary>B3D6: solid right next to it (x-1 or x+17) at height y.</summary>
    private bool SideSolid(Enemy e, int y)
    {
        int x = (e.Dir & 1) != 0 ? Math.Min(e.X + 0x11, 0xF8) : Math.Max(e.X - 1, 0);
        return EnemyTile(e, x, y) >= 2;
    }

    /// <summary>B4BB: the hop — vx 0xF0 (zeroed if there is a wall ahead), Y by the table; lands on the descent.</summary>
    private void GuardHop(Enemy e)
    {
        e.Moving = true;
        e.Frame = 10 + (e.Dir & 1) * 2;
        if (e.Timer == 0)
        {
            GuardEndHop(e);
            return;
        }

        e.Timer--;
        e.SpeedX = GuardHopSpeed;
        if (HopBlocked(e))
            e.SpeedX = 0;
        if (e.Timer < 8 && GuardGroundAt(e, HopHigh[e.Timer]))
        {
            GuardLand(e);
            return;
        }

        e.Y += (e.HopLow ? HopLowTable : HopHigh)[e.Timer];
    }

    /// <summary>B64E: wall ahead at the height of the feet of the next position.</summary>
    private bool HopBlocked(Enemy e)
    {
        var (x, _, free) = NextPosition(e);
        if (free)
            return false;

        int y = e.Y + HopHigh[e.Timer] + 15;
        if ((e.Dir & 1) != 0)
        {
            if (x + 16 > 0xFF)
                return false;
            x += 16;
        }

        return EnemyTile(e, x, y) >= 2;
    }

    /// <summary>B50B: end of the hop with no ground — starts falling (vy 0x500).</summary>
    private void GuardEndHop(Enemy e)
    {
        e.Timer = 0;
        e.Moving = true;
        if (GuardGroundAt(e, HopHigh[0]))
        {
            GuardLand(e);
            return;
        }

        GuardStartFall(e);
    }

    private void GuardStartFall(Enemy e)
    {
        e.Timer = 0;
        e.Moving = true;
        e.SpeedY = EnemyFallSpeedFixed;
        e.SpeedX = 0;
        e.Dir = 1;
        e.Phase = 4;
        e.State = EnemyState.Falling;
        PlaySound(OriginalSounds.EnemyFall, e.Room);
    }

    /// <summary>B52F: lands — Y rounded down to the cell, waits 8 ticks.</summary>
    private void GuardLand(Enemy e)
    {
        e.SpeedX = 0;
        e.Moving = false;
        e.Timer = GuardGroundWait;
        e.Y = (e.Y + 7) & ~7;
        e.Phase = 1;
        e.State = EnemyState.Pausing;
        PlaySound(OriginalSounds.EnemyLanded, e.Room);
    }

    /// <summary>B5B7: ladder; leaves when it ends (B0F9), reverses if it hits.</summary>
    private void GuardClimb(Enemy e)
    {
        e.Moving = true;
        e.Frame = 13 + ((TickCount & 4) != 0 ? 1 : 0);
        if (ClimbEnds(e))
        {
            SnapEnemy(e);
            e.SpeedX = 0;
            e.SpeedY = 0;
            e.Moving = false;
            e.Timer = GuardGroundWait;
            e.HopX = 0xFF;
            e.Phase = 1;
            e.State = EnemyState.Pausing;
            return;
        }

        if (!CanMove(e))
            e.Dir ^= 1;
    }

    /// <summary>B5FB: falls until it finds ground; then goes back to the hop direction and lands.</summary>
    private void GuardFall(Enemy e)
    {
        if (!GuardGroundAt(e, HopHigh[0]))
            return;

        e.SpeedY = 0;
        e.Dir = e.SavedDir;
        GuardLand(e);
    }

    /// <summary>
    /// B555/B571: ground under the feet (x+4 and x+12, y+dy+16). A ladder in the middle counts as empty; the top of
    /// the ladder (no ladder above) counts as ground.
    /// </summary>
    private bool GuardGroundAt(Enemy e, int dy)
    {
        int y = Math.Min(e.Y + dy + 16, 0xB8);
        return GroundProbe(e, e.X + 4, y) || GroundProbe(e, e.X + 12, y);
    }

    private bool GroundProbe(Enemy e, int x, int y)
    {
        int t = EnemyTile(e, x, y);
        if (t == 0)
            return false;
        if (t != 1)
            return true;
        return EnemyTile(e, x, y - 8) != 1;
    }
}
