using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// AI <b>ported from the MSX2 ROM</b> (bank 3): sarcophagi (<c>671D</c>), white mummy — type 1
/// (<c>AD80</c>), sarcophagus exit shared by types 1 and 2 (<c>AE35</c>/<c>AE55</c>), death effect
/// (<c>6608</c>/<c>662D</c>) and the movement integrator (<c>656C</c>). The terrain tests are the
/// same as the original (<c>B15E</c>, <c>B165</c>, <c>B17A</c>, <c>9933</c>).
/// </summary>
public sealed partial class StageSession
{
    private const int CoffinFirstCount = 0x14;   // 6772: contador inicial (x4 ticks = 80)
    private const int CoffinNextCount = 0x30;    // 6739: after releasing a mummy (x4 = 192)
    private const int EmergeStepTicks = 0x20;    // AD49/AE4D/AE60
    private const int PauseTicks = 0x10;         // ADED/AF7D
    private const int MaxWallBumps = 0x10;       // ADE5
    private const int MummyWalkSpeed = 0x180;    // ADA0
    private const int EnemyFallSpeedFixed = 0x500; // AF44
    private static readonly int[] VanishFrames = { 0x15, 0x16, 0x16 }; // 6641 (MSX2)

    /// <summary>671D: every 4 ticks the armed sarcophagi count; at zero they release a mummy.</summary>
    private void UpdateCoffinsOriginal()
    {
        if (TickCount % 4 != 0)
            return;

        for (int i = 0; i < Coffins.Count; i++)
        {
            var c = Coffins[i];
            if (!c.Armed || --c.Cooldown > 0)
                continue;

            c.Armed = false;
            c.Cooldown = CoffinNextCount;
            SpawnFromCoffin(i);
        }
    }

    /// <summary>AC6D/AD2C: the mummy is born invisible at the record's y, facing the player (type 1: the other way).</summary>
    private void SpawnFromCoffin(int index)
    {
        var c = Coffins[index];
        int towardPlayer = DirectionToPlayer(c.Position.Room, c.Position.X);
        var e = new Enemy
        {
            Kind = c.Kind,
            Room = c.Position.Room,
            X = c.Position.X,
            Y = c.Position.Y,
            CoffinIndex = index,
            State = EnemyState.Emerging,
            Phase = 0,
            Timer = EmergeStepTicks,
            Frame = EnemyFrames.Hidden,
            Dir = 2 + (c.Kind == EnemyKind.Mummy ? towardPlayer ^ 1 : towardPlayer),
        };
        e.Facing = e.Dir == 3 ? Facing.Right : Facing.Left;
        if (c.Kind == EnemyKind.Guard)
        {
            InitGuard(e, towardPlayer);   // B274: no sarcophagus animation
        }
        else if (c.Kind == EnemyKind.Rock)
        {
            // B67E: it also appears in a cloud, with no sarcophagus animation.
            e.Timer = 0;
            e.Flags = 0;
            e.Frame = 0x18;
        }
        else
        {
            c.Look = CoffinLook.Ajar;
        }

        c.Open = true;
        Enemies.Add(e);
        if (c.Position.Room == CurrentRoom)
            Emit(GameEventKind.CoffinOpened, c.Position.X, c.Position.Y,
                sound: c.Kind is EnemyKind.Guard or EnemyKind.Rock ? OriginalSounds.GuardSpawn : OriginalSounds.EnemySpawn);
    }

    /// <summary>B223: 0 = left, 1 = right, comparing the room column on the grid and then the X.</summary>
    private int DirectionToPlayer(int room, int x)
    {
        int enemyColumn = RoomColumn(room), playerColumn = RoomColumn(CurrentRoom);
        if (enemyColumn != playerColumn)
            return playerColumn < enemyColumn ? 0 : 1;

        return Player.X < x ? 0 : 1;
    }

    private int RoomColumn(int room)
    {
        var grid = Definition.RoomGrid;
        for (int r = 0; r < grid.GetLength(0); r++)
            for (int c = 0; c < grid.GetLength(1); c++)
                if (grid[r, c] == room)
                    return c;
        return 0;
    }

    /// <summary>Uses the ROM-ported AI?</summary>
    private static bool UsesOriginalAi(Enemy e) =>
        e.Vanishing || e.Kind.IsHostile();

    /// <summary>Um tick da IA original seguido do integrador (656C).</summary>
    private void UpdateOriginalEnemy(Enemy e)
    {
        if (e.Vanishing)
        {
            StepVanish(e);
            return;
        }

        if (e.Kind == EnemyKind.Guard)
        {
            UpdateGuard(e);
        }
        else if (e.Kind == EnemyKind.Rock)
        {
            UpdateRock(e);
        }
        else if (e.Kind == EnemyKind.GoldMummy)
        {
            switch (e.Phase)   // AE23
            {
                case 0: EmergeHidden(e); break;
                case 1: EmergeShown(e); break;
                case 2: GoldPause(e); break;
                case 3: GoldWalk(e); break;
                case 4: GoldClimb(e); break;
                case 5: MummyFall(e); break;
            }
        }
        else
        {
            switch (e.Phase)   // AD80
            {
                case 0: EmergeHidden(e); break;
                case 1: EmergeShown(e); break;
                case 2: MummyPause(e); break;
                case 3: MummyWalk(e); break;
                case 4: MummyFall(e); break;
            }
        }

        if (!e.Vanishing && e.Moving)
            Integrate(e);
        e.Facing = e.Dir == 3 ? Facing.Right : e.Dir == 2 ? Facing.Left : e.Facing;
    }

    /// <summary>AE35: 0x20 ticks invisible; then the sarcophagus opens and the mummy appears facing front.</summary>
    private void EmergeHidden(Enemy e)
    {
        if (--e.Timer > 0)
            return;

        SetCoffinLook(e, CoffinLook.Open);
        PlaySound(OriginalSounds.CoffinOpen, e.Room);
        e.Flags = 2;
        e.Frame = EnemyFrames.Front;
        e.Timer = EmergeStepTicks;
        e.Phase = 1;
    }

    /// <summary>AE55/AE6F: 0x20 more ticks in front of the sarcophagus, which then CLOSES.</summary>
    private void EmergeShown(Enemy e)
    {
        if (--e.Timer > 0)
            return;

        SetCoffinLook(e, CoffinLook.Closed);
        PlaySound(OriginalSounds.CoffinClose, e.Room);
        e.Flags = 3;
        e.Timer = EmergeStepTicks;
        e.Phase = 2;
        e.State = EnemyState.Pausing;
    }

    /// <summary>AD90: stopped facing front; at the end it tries to turn and walk (if stuck on both sides, it vanishes).</summary>
    private void MummyPause(Enemy e)
    {
        if (NothingBelow(e))
        {
            StartEnemyFall(e);
            return;
        }

        e.Frame = EnemyFrames.Front;
        if (--e.Timer > 0)
            return;

        e.SpeedX = MummyWalkSpeed;
        e.Dir ^= 1;
        if (CanMove(e))
        {
            StartWalking(e);
            return;
        }

        e.Dir ^= 1;
        if (CanMove(e))
            StartWalking(e);
        else if (TryTraverseRoom(e))
            StartWalking(e);
        else
            Vanish(e, withSound: true);
    }

    private static void StartWalking(Enemy e)
    {
        e.Phase = 3;
        e.State = EnemyState.Walking;
    }

    /// <summary>ADBB: walks; with no ground it falls, at the wall it stops 0x10 ticks (on the 16th hit it vanishes).</summary>
    private void MummyWalk(Enemy e)
    {
        e.Moving = true;
        e.Frame = (e.Dir == 3 ? 3 : 1) + ((TickCount & 4) != 0 ? 1 : 0);   // AF14
        if (NothingBelow(e))
        {
            SnapEnemy(e);
            StartEnemyFall(e);
            return;
        }

        if (CanMove(e))
            return;

        // B00F/B0A6: if the mummy is blocked at the wall and the player is in the neighboring room,
        // it crosses to the player's room (delta from 0xB0F1, 4 corners via 98E6).
        if (TryTraverseRoom(e))
        {
            StartWalking(e);
            return;
        }

        if (++e.Bumps >= MaxWallBumps)
        {
            Vanish(e, withSound: true);
            return;
        }

        PauseEnemy(e);
    }

    /// <summary>ADFA: falls at 5 px/tick; on touching the ground it snaps, stops and turns opposite to the fall.</summary>
    private void MummyFall(Enemy e)
    {
        e.Moving = true;
        if (FallContinues(e))
            return;

        SnapEnemy(e);
        PlaySound(OriginalSounds.EnemyLanded, e.Room);
        PauseEnemy(e);
        e.Moving = false;
        // ADFA (type 1): (saved^1)|2 · AF9B (type 2): (saved^1)|3 — the type 2 pause redoes the direction.
        e.Dir = (e.SavedDir ^ 1) | (e.Kind == EnemyKind.GoldMummy ? 3 : 2);
    }

    /// <summary>ADEA: snaps to the cell, stops 0x10 ticks (state 2).</summary>
    private static void PauseEnemy(Enemy e)
    {
        SnapEnemy(e);
        e.Timer = PauseTicks;
        e.Moving = false;
        e.Phase = 2;
        e.State = EnemyState.Pausing;
    }

    /// <summary>AF2C: starts falling (speed 0x500), saving the direction.</summary>
    private void StartEnemyFall(Enemy e)
    {
        PlaySound(OriginalSounds.EnemyFall, e.Room);
        e.Phase = e.Kind == EnemyKind.GoldMummy ? 5 : 4;   // AF2C: A=4 (type 1) / AF2A: A=5 (type 2)
        e.State = EnemyState.Falling;
        e.SpeedX = 0;
        e.SavedDir = e.Dir;
        e.Dir = 1;
        e.SpeedY = EnemyFallSpeedFixed;
    }

    /// <summary>6608/6611: turns into the spark effect (no longer kills, cannot be hit).</summary>
    private void Vanish(Enemy e, bool withSound)
    {
        if (e.CoffinIndex >= 0 && e.Phase < 2)
            SetCoffinLook(e, CoffinLook.Closed);   // 9B95: hit while still in the sarcophagus
        if (e.Kind == EnemyKind.Rock)
            ReleaseRock(e);
        e.Vanishing = true;
        e.State = EnemyState.Dying;
        e.Flags = 0;
        e.Timer = 0;
        e.Moving = false;
        e.Frame = VanishFrames[0];
        if (withSound && e.Room == CurrentRoom)
            Emit(GameEventKind.EnemyVanished, e.X, e.Y, sound: OriginalSounds.EnemyVanish);
    }

    /// <summary>662D/B6E0: frames 0x15, 0x16, 0x16 (8 ticks each); at the end the sarcophagus re-arms (6706).</summary>
    private void StepVanish(Enemy e)
    {
        int index = ++e.Timer >> 3;
        if (index < VanishFrames.Length)
        {
            e.Frame = VanishFrames[index];
            return;
        }

        Enemies.Remove(e);
        if (e.CoffinIndex >= 0 && e.CoffinIndex < Coffins.Count)
        {
            Coffins[e.CoffinIndex].Armed = true;
            Coffins[e.CoffinIndex].Open = false;
        }
    }

    private void SetCoffinLook(Enemy e, CoffinLook look)
    {
        if (e.CoffinIndex >= 0 && e.CoffinIndex < Coffins.Count)
            Coffins[e.CoffinIndex].Look = look;
    }

    // ── Integrator and terrain tests of the original ──────────────────────────────────────────

    /// <summary>656C: adds the speed; leaving through the edge enters the neighbor room.</summary>
    private void Integrate(Enemy e)
    {
        if (e.Dir >= 2)
        {
            int x = (e.X << 8) + e.XFraction + (e.Dir == 3 ? e.SpeedX : -e.SpeedX);
            if (x < 0)
            {
                MoveEnemyToRoom(e, -1, 0);
                x = 0xF000;
            }
            else if (x >> 8 >= 0xF1)
            {
                MoveEnemyToRoom(e, 1, 0);
                x = 0;
            }

            e.X = x >> 8;
            e.XFraction = x & 0xFF;
            return;
        }

        int y = (e.Y << 8) + e.YFraction + (e.Dir == 1 ? e.SpeedY : -e.SpeedY);
        if (y < 0)
        {
            MoveEnemyToRoom(e, 0, -1);
            y = 0xB000;
        }
        else if (y >> 8 >= 0xB1)
        {
            MoveEnemyToRoom(e, 0, 1);
            y = 0;
        }

        e.Y = y >> 8;
        e.YFraction = y & 0xFF;
    }

    private void MoveEnemyToRoom(Enemy e, int dx, int dy) =>
        e.Room = RoomGridNavigator.Neighbor(Definition.RoomGrid, e.Room, dx, dy);

    /// <summary>
    /// B00F/B0A6 (bank 2): the mummy blocked at the wall tries to cross to the player's room.
    /// Returns true if it crossed (the position and the room were updated).
    /// </summary>
    private bool TryTraverseRoom(Enemy e)
    {
        // B00F bails out if the enemy is already in the player's room.
        if (e.Room == CurrentRoom) return false;
        // B00F bails out if IX+11 (the direction) is 0.
        if (e.Dir == 0) return false;

        int px = Player.X, py = Player.Y;
        int ex = e.X, ey = e.Y;

        // ROM: 0=down, 1=up, 2=right, 3=left ; port: 0=up, 1=down, 2=left, 3=right.
        // The mapping is port ^ 1 (0<->1, 2<->3).
        int romDir = e.Dir ^ 1;

        int returned = 0;
        // direction 1 (ROM down -> port up): enemy at the bottom, player at the top.
        if (romDir == 1 && ey >= 0x98 && py < 0x10 && Math.Abs(ex - px) < 0x30) returned = 2;
        // direction 2 (ROM up -> port down): enemy on the left, player on the right.
        else if (romDir == 2 && ex < 0x18 && px >= 0xD8 && Math.Abs(ey - py) < 0x30) returned = 3;
        // direction 3 (ROM right -> port left): enemy on the right, player on the left.
        else if (romDir == 3 && ex >= 0xD8 && px < 0x18 && Math.Abs(ey - py) < 0x30) returned = 4;

        if (returned == 0) return false;

        int dx, dy;
        switch (returned)
        {
            case 2: dx = 0; dy = -1; break;   // up
            case 3: dx = 1; dy = 0; break;    // right
            case 4: dx = -1; dy = 0; break;   // left
            default: return false;
        }

        int neighbor = RoomGridNavigator.Neighbor(Definition.RoomGrid, e.Room, dx, dy);
        if (neighbor != CurrentRoom) return false;

        // B0A6: delta de 0xB0F1: dir 2 -> f0 00 (-0x10 Y), dir 3 -> 00 10 (+0x10 X), dir 4 -> 00 f0 (-0x10 X).
        int nx = ex, ny = ey;
        switch (returned)
        {
            case 2: ny -= 0x10; break;
            case 3: nx += 0x10; break;
            case 4: nx -= 0x10; break;
        }
        if (nx < 0 || nx > 255 || ny < 0 || ny > 191) return false;

        // B0E5 (98E6): the 4 corners of the box (16x16) must be empty (or mechanism).
        if (!EnemyPassable(nx, ny) || !EnemyPassable(nx + 15, ny) ||
            !EnemyPassable(nx, ny + 15) || !EnemyPassable(nx + 15, ny + 15))
            return false;

        e.X = nx;
        e.Y = ny;
        MoveEnemyToRoom(e, dx, dy);
        return true;
    }

    /// <summary>98E6: the 2-bit cell is empty or a mechanism (not wall/ladder/diggable).</summary>
    private bool EnemyPassable(int x, int y)
    {
        int v = EnemyTile(new Enemy { Room = CurrentRoom, Kind = EnemyKind.Mummy }, x, y);
        return v == 0 || v == 3;
    }

    /// <summary>2-bit value of the cell in the enemy's room (98E6).</summary>
    private int EnemyTile(Enemy e, int x, int y)
    {
        var map = _rooms[e.Room];
        int v = map.At(Math.Clamp(x, 0, 255), Math.Clamp(y, 0, 191)) switch
        {
            TerrainKind.Ladder => 1,
            TerrainKind.Soft => 2,
            TerrainKind.Wall => 3,
            _ => 0,
        };
        if (v == 0 && Enemies.Any(o => o.Kind == EnemyKind.MovableBlock && o.Room == e.Room
                && x >= o.X && x < o.X + 16 && y >= o.Y && y < o.Y + 16))
            v = 3;
        return v;
    }

    /// <summary>B15E: nothing under the feet at x+2 and x+10 (ladder counts as ground).</summary>
    private bool NothingBelow(Enemy e)
    {
        int feet = Math.Min(e.Y + 16, 0xB8);
        return EnemyTile(e, e.X + 2, feet) == 0 && EnemyTile(e, e.X + 10, feet) == 0;
    }

    /// <summary>B1AC: position of the next tick (if moving); <c>free</c> = left the screen (passes).</summary>
    private (int X, int Y, bool Free) NextPosition(Enemy e)
    {
        if (!e.Moving)
            return (e.X, e.Y, false);

        if (e.Dir < 2)
        {
            int y = ((e.Y << 8) + e.YFraction + (e.Dir == 1 ? e.SpeedY : -e.SpeedY)) >> 8;
            return (e.X, y, y >= 0xC0 || y < 0);
        }

        int x = ((e.X << 8) + e.XFraction + (e.Dir == 3 ? e.SpeedX : -e.SpeedX)) >> 8;
        return (x, e.Y, Math.Abs(x - e.X) >= 0x80 || x < 0);
    }

    /// <summary>B165/9933: can it walk in the current direction (3 tips at the target edge, solid = 2/3)?</summary>
    private bool CanMove(Enemy e)
    {
        var (x, y, free) = NextPosition(e);
        if (free)
            return true;

        foreach (var (px, py) in EdgeProbes(e.Dir, x, y))
            if (EnemyTile(e, px, py) >= 2)
                return false;
        return true;
    }

    /// <summary>B17A: the fall continues (nothing under the feet at the next position, x+2 and x+10)?</summary>
    private bool FallContinues(Enemy e)
    {
        var (x, y, free) = NextPosition(e);
        if (free)
            return true;

        int feet = Math.Min(y + 16, 0xB8);
        return EnemyTile(e, x + 2, feet) == 0 && EnemyTile(e, x + 10, feet) == 0;
    }

    /// <summary>995A/998E: the 3 test tips at the edge of the direction.</summary>
    private static IEnumerable<(int X, int Y)> EdgeProbes(int dir, int x, int y)
    {
        switch (dir)
        {
            case 0:
                for (int i = 0; i < 3; i++) yield return (x + 2 + 6 * i, Math.Max(y - 1, 0));
                break;
            case 1:
                for (int i = 0; i < 3; i++) yield return (x + 2 + 6 * i, Math.Min(y + 16, 0xB8));
                break;
            case 2:
                for (int i = 0; i < 3; i++) yield return (Math.Max(x - 1, 0), y + 6 * i);
                break;
            default:
                for (int i = 0; i < 3; i++) yield return (Math.Min(x + 16, 0xF8), y + 6 * i);
                break;
        }
    }

    /// <summary>B833: snaps to the cell, rounding in the direction of movement.</summary>
    private static void SnapEnemy(Enemy e)
    {
        if (e.Dir >= 2)
            e.X = e.Dir == 2 ? e.X & ~7 : (e.X + 7) & ~7;
        else
            e.Y = e.Dir == 0 ? e.Y & ~7 : (e.Y + 7) & ~7;
    }
}
