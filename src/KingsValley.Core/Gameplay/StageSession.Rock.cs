using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// Type 4 — the stone (<c>B68E</c>), ported from the ROM. It appears in a cloud, turns red (colors 0x07/0x49)
/// and <b>becomes a terrain block</b> (writes wall into the 2x2 cells, B9A2): blinks 0x3C ticks, rolls at the
/// player at 0x200, hits and becomes a block for 0xB4 ticks; falls at 0x500 and bounces (table 0xB926).
/// States: 0 appearing, 1 blinking, 2 rolling (B754), 3 falling (B881), 4 block (B8EC), 5 bouncing (B90E).
/// </summary>
public sealed partial class StageSession
{
    private static readonly int[] RockAppearFrames = { 0x18, 0x19, 0x18, 0x19, 0x18, 0x19 };   // B6B6
    private static readonly int[] RockBounce = { -3, -2, -1, -1, 0, 0, 1, 1, 2, 3, -2, -1, 0, 1, 2, -1, 0, 1 }; // B926
    private const int RockBlinkTicks = 0x3C;
    private const int RockBlockTicks = 0xB4;
    private const int RockRollSpeed = 0x200;
    private const int RockMaxCycles = 3;   // B724: on the 4th time it vanishes

    private void UpdateRock(Enemy e)
    {
        switch (e.Phase)
        {
            case 0: RockAppear(e); break;
            case 1: RockBlink(e); break;
            case 2: RockRoll(e); break;
            case 3: RockFall(e); break;
            case 4: RockRest(e); break;
            case 5: RockBounceStep(e); break;
        }
    }

    /// <summary>B6A0: cloud (8 ticks per animation step); then it becomes a red block and starts blinking.</summary>
    private void RockAppear(Enemy e)
    {
        int index = ++e.Timer >> 3;
        if (index < RockAppearFrames.Length)
        {
            e.Frame = RockAppearFrames[index];
            return;
        }

        e.Flags = 3;
        e.Timer = RockBlinkTicks;
        e.Yellow = true;   // here: colors 0x07/0x49 (color type 6)
        if (!PlaceRock(e))
            return;
        e.Phase = 1;
        e.State = EnemyState.Pausing;
        e.Frame = 15;
    }

    /// <summary>B6F9: blinking block; with no ground it falls; at the end of the time it rolls at the player.</summary>
    private void RockBlink(Enemy e)
    {
        if (RockFreeBelow(e))
        {
            ReleaseRock(e);
            RockStartFall(e);
            return;
        }

        if (--e.Timer > 0)
        {
            e.Flags = 3;
            e.Frame = (e.Timer & 0x10) == 0 ? 15 : 16;
            return;
        }

        ReleaseRock(e);
        if (++e.RollCycles > RockMaxCycles)
        {
            Vanish(e, withSound: true);
            return;
        }

        e.Dir = DirectionToPlayer(e.Room, e.X) | 2;
        if (!CanMove(e))
        {
            e.Dir ^= 1;
            if (!CanMove(e))
            {
                Vanish(e, withSound: true);
                return;
            }
        }

        e.SpeedX = RockRollSpeed;
        e.Moving = true;
        e.Phase = 2;
        e.State = EnemyState.Walking;
    }

    /// <summary>B754: rolls; falls if it loses the ground (only with x&amp;7 &lt; 3); hits and becomes a block.</summary>
    private void RockRoll(Enemy e)
    {
        e.Timer--;
        e.Frame = 0x11 + ((e.Timer >> 2) & 3);   // B874
        if ((e.X & 7) < 3 && RockFreeBelow(e))
        {
            RockStartFall(e);
            return;
        }

        if (!CanMove(e))
        {
            SnapEnemy(e);
            if (RockFreeBelow(e))
            {
                RockStartFall(e);
                return;
            }

            RockBecomeBlock(e);
            PlaySound(OriginalSounds.RockStop, e.Room);
            return;
        }

        var other = RockCollision(e);
        if (other == null)
            return;

        e.X = e.Dir == 2 ? (e.X + 7) & ~7 : e.X & ~7;   // B781
        if (other.Moving && other.Dir >= 2)
            other.X = other.Dir == 2 ? (other.X + 7) & ~7 : other.X & ~7;
        RockBecomeBlock(e);
        PlaySound(OriginalSounds.RockStop, e.Room);
    }

    /// <summary>B856: starts falling (vy 0x500).</summary>
    private void RockStartFall(Enemy e)
    {
        e.RollCycles = 0;
        e.Dir = 1;
        e.SpeedY = EnemyFallSpeedFixed;
        e.SpeedX = 0;
        e.FallTicks = 0;
        e.Moving = true;
        e.Phase = 3;
        e.State = EnemyState.Falling;
        PlaySound(OriginalSounds.EnemyFall, e.Room);
    }

    /// <summary>B881: falls; on touching the ground it snaps and bounces (state 5).</summary>
    private void RockFall(Enemy e)
    {
        if (e.FallTicks < 0xFF)
            e.FallTicks++;
        e.Moving = true;
        e.Frame = 0x11 + ((e.Timer >> 2) & 3);
        if (CanMove(e))
        {
            if (RockCollision(e) == null)
                return;

            SnapEnemy(e);
            RockBecomeBlock(e);
            if (e.FallTicks >= 3)
                PlaySound(OriginalSounds.RockStop, e.Room);
            return;
        }

        SnapEnemy(e);
        e.Moving = false;
        e.Frame = 0x11;
        e.Timer = 0;
        e.Phase = 5;
        e.State = EnemyState.Airborne;
        PlaySound(OriginalSounds.CoffinClose, e.Room);   // B8C2 → 4294 (same sound)
    }

    /// <summary>B90E: three bounces (table 0xB926) and it becomes a block.</summary>
    private void RockBounceStep(Enemy e)
    {
        if (e.Timer == RockBounce.Length)
        {
            RockBecomeBlock(e);
            return;
        }

        e.Y += RockBounce[e.Timer++];
    }

    /// <summary>B8EC: block; with no ground it falls; after 0xB4 ticks it blinks again.</summary>
    private void RockRest(Enemy e)
    {
        if (RockFreeBelow(e))
        {
            ReleaseRock(e);
            RockStartFall(e);
            return;
        }

        if (--e.Timer > 0)
            return;

        e.Timer = RockBlinkTicks;
        e.Phase = 1;
        e.State = EnemyState.Pausing;
    }

    /// <summary>B938/B941: writes the block into the map and stays still 0xB4 ticks (dangerous, not hittable).</summary>
    private void RockBecomeBlock(Enemy e)
    {
        if (!PlaceRock(e))
            return;
        RockAsBlock(e);
    }

    private static void RockAsBlock(Enemy e)
    {
        e.Moving = false;
        e.Frame = 0x11;
        e.Flags = 1;
        e.Timer = RockBlockTicks;
        e.Phase = 4;
        e.State = EnemyState.Pausing;
    }

    /// <summary>B8C5: the knife does not destroy the stone — it stops and becomes a block (and stops being hittable).</summary>
    private void RockHit(Enemy e)
    {
        e.Flags = 1;
        if (e.Phase == 1)
        {
            e.X &= ~7;
            e.Y &= ~7;
            RockAsBlock(e);
        }
        else if (e.Phase == 2)
        {
            e.X &= ~7;
            e.Y &= ~7;
            RockBecomeBlock(e);
        }
    }

    /// <summary>B9A2 + BA6E: writes wall into the 2x2 cells; if there was already solid there, it vanishes (BA7F).</summary>
    private bool PlaceRock(Enemy e)
    {
        var map = _rooms[e.Room];
        int c = e.X >> 3, r = e.Y >> 3;
        bool overlap = false;
        for (int i = 0; i < 4; i++)
            overlap |= map.Get(c + (i & 1), r + (i >> 1)).IsSolid();
        for (int i = 0; i < 4; i++)
            map.SetRock(c + (i & 1), r + (i >> 1), true);
        e.RockCell = (c, r);

        if (!overlap)
            return true;

        ReleaseRock(e);
        Vanish(e, withSound: true);
        return false;
    }

    /// <summary>B9F0: gives the cells back to the original terrain.</summary>
    private void ReleaseRock(Enemy e)
    {
        if (e.RockCell is not { } cell)
            return;

        var map = _rooms[e.Room];
        for (int i = 0; i < 4; i++)
            map.SetRock(cell.Column + (i & 1), cell.Row + (i >> 1), false);
        e.RockCell = null;
    }

    /// <summary>B168 (C=1): nothing solid right below (x+2, x+8, x+14 at y+16).</summary>
    private bool RockFreeBelow(Enemy e)
    {
        int y = Math.Min(e.Y + 16, 0xB8);
        for (int i = 0; i < 3; i++)
            if (EnemyTile(e, e.X + 2 + 6 * i, y) >= 2)
                return false;
        return true;
    }

    /// <summary>B7DF: another stone (from another sarcophagus, same room) closer than 15 px to the next position.</summary>
    private Enemy RockCollision(Enemy e)
    {
        var (x, y, free) = NextPosition(e);
        if (free)
            return null;

        foreach (var o in Enemies)
        {
            if (o == e || o.Kind != EnemyKind.Rock || o.Vanishing || o.CoffinIndex == e.CoffinIndex || o.Room != e.Room)
                continue;
            if (Math.Abs(y - o.Y) >= 15 || Math.Abs(x - o.X) >= 15)
                continue;
            if (o.Moving && o.Dir == e.Dir)
                return null;
            return o;
        }

        return null;
    }
}
