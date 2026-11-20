using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>Player movement and actions.</summary>
public sealed partial class StageSession
{
    private const int StepEveryTicks = 4;

    /// <summary>A5A2: probe for climbing the ladder (y + 0x0A); going down (A591) uses y + 0x10.</summary>
    private const int LadderUpProbe = 0x0A;

    private void UpdatePlayer(PlayerInput input)
    {
        var p = Player;
        switch (p.State)
        {
            case PlayerState.Dying:
                StepDeath();
                return;
            case PlayerState.Exiting:
                // 71DE: the exit machine runs in the state 9 handler (4B06 → clear_tick), which does NOT
                // have the `LD A,(0xE203); RRA; RET nc` gate — but that does not speed it up: the gate of the
                // game states halves a dispatcher that already runs at ~30 Hz (the H.TIMI ISR is held
                // at E206 while the previous frame has not finished; measured: E203 ~30.7 increments/s).
                // So clear_tick runs at the SAME rate as the other handlers: 1 call = 1 tick, and the
                // exit counters (E2F7, E259, E204) count 1 per call — 266 units ≈ 8.9 s.
                // Measured on the longplay: the text step (E204 = 0x3C + 0x78 = 180 units) takes 6 s
                // between the door closing and "NICE! LET'S GO NEXT STAGE" → 30 units/s ✓.
                StepExit(input);
                return;
            case PlayerState.Digging:
                // A42B: the pose alternates when bit 1 of the timer clears; the item (AA66..ABF5) decides the end.
                p.StateTick++;
                if ((Physics.DigTicks - p.StateTick & 3) == 0)
                    PlaySound(DigSound(p.Holding));   // A405..A428: every 4 ticks
                if ((Physics.DigTicks - p.StateTick & 2) == 0)
                    p.Pose ^= 1;
                if (StepDig())
                {
                    // AA8C: the item ran out — idle pose, empty hands.
                    p.Holding = ItemKind.None;
                    p.State = PlayerState.Standing;
                    p.Pose = PlayerPoses.Walk(p.Facing, 0);
                }

                return;
            case PlayerState.Throwing:
                // A384: Vick stays 8 logic CALLS standing still (E286 = 8, decremented once per
                // call — and 1 call = 1 tick); with E286 == 4 the pose advances (inc E285) and with
                // E286 == 0 the tool is released (throw_done).
                p.StateTick++;
                if (p.StateTick == Physics.ThrowPoseTick)
                    p.Pose++;
                if (p.StateTick >= Physics.ThrowTicks)
                    FinishThrow();

                return;
            case PlayerState.Jumping:
                StepJump();
                break;
            case PlayerState.Falling:
                StepFall();
                break;
            case PlayerState.Climbing:
                StepClimb(input);
                break;
            default:
                StepStanding(input);
                break;
        }

        if (Status != StageStatus.Playing || p.State == PlayerState.Exiting)
            return;

        CollectAt(p.Bounds);
        if (AtOpenDoor())
        {
            Clear();
            return;
        }

        CheckRoomExit();
    }

    private void StepStanding(PlayerInput input)
    {
        var p = Player;
        // Pushing a swivel door: advances through it while it rotates (0xBB43/0xBB93).
        foreach (var m in RoomMechanisms)
        {
            if (m.Kind is not (MechanismKind.RotatingDoor or MechanismKind.Unknown2)) continue;
            if (!_mechanismStates.TryGetValue(m, out var st) || !st.Pushing) continue;
            if (System.Math.Abs(p.X - m.Position.X) > 24) continue;
            int dir = st.PushRight ? 1 : -1;
            p.Facing = dir < 0 ? Facing.Left : Facing.Right;
            p.X += dir * Physics.WalkSpeed;
            p.AnimPhase = (p.AnimPhase + 1) & 3;
            p.Pose = PlayerPoses.Walk(p.Facing, p.AnimPhase);
            return;
        }

        if (!Grounded(p.X, p.Y) && !LadderAtBody(p.X, p.Y))
        {
            // 9FD4: when starting to fall the pose is the idle one and the walk cycle restarts.
            p.State = PlayerState.Falling;
            p.Pose = PlayerPoses.Stand(p.Facing);
            p.AnimPhase = 0;
            p.AnimTimer = 7;
            PlaySound(OriginalSounds.PlayerFall);
            return;
        }

        if (input.Action)
        {
            OnAction(input);
            return;
        }

        if (input.Vertical != 0 && TryStartClimb(input.Vertical))
            return;

        int h = input.Horizontal;
        if (h == 0)
        {
            p.AnimTimer = 1;   // 9F3F: standing, the next key already changes the pose
            return;
        }

        p.Facing = h < 0 ? Facing.Left : Facing.Right;
        AdvanceWalkPose();
        int newX = p.X + h * Physics.WalkSpeed;
        // 9F9A/9910: 3 tips at the edge (y, y+6, y+12); blocked = snaps to the cell.
        if (!WallAhead(newX, p.Y, h))
        {
            p.X = newX;
            p.StepCounter++;   // the original has no footstep sound
        }
        else
        {
            p.X = SnapToCell(newX, p.Facing);
            TryPushBlock(h);
        }
    }

    private static int DigSound(ItemKind tool) => tool switch
    {
        ItemKind.Shovel => OriginalSounds.DigShovel,
        ItemKind.Pickaxe => OriginalSounds.DigPickaxe,
        ItemKind.Hammer => OriginalSounds.DigHammer,
        _ => OriginalSounds.DigDrill,
    };

    /// <summary>9F19: the walk cycle advances every 4 ticks with a direction pressed.</summary>
    private void AdvanceWalkPose()
    {
        var p = Player;
        if (--p.AnimTimer > 0)
            return;

        p.AnimTimer = StepEveryTicks;
        p.AnimPhase = (p.AnimPhase + 1) & 3;
        p.Pose = PlayerPoses.Walk(p.Facing, p.AnimPhase);
    }

    /// <summary>A1AA/A304: on touching the ground the cycle restarts at the idle pose.</summary>
    private void Land()
    {
        var p = Player;
        p.State = PlayerState.Standing;
        p.AnimPhase = 0;
        p.AnimTimer = 1;
        AdvanceWalkPose();
    }

    private void OnAction(PlayerInput input)
    {
        var p = Player;
        if (p.Holding == ItemKind.None)
        {
            // A008: with empty hands the button jumps (with an item in hand the original does not jump).
            p.State = PlayerState.Jumping;
            p.StateTick = 0;
            p.JumpDirection = input.Horizontal;
            p.XFraction = 0;
            p.YFraction = 0;
            p.VelocityY = -Physics.JumpVelocityFixed;
            if (input.Horizontal != 0)
                p.Facing = input.Horizontal < 0 ? Facing.Left : Facing.Right;
            p.Pose = PlayerPoses.Jump(p.Facing);
            Emit(GameEventKind.Jumped, p.X, p.Y, sound: OriginalSounds.Jump);
            return;
        }

        if (p.Holding.IsWeapon())
        {
            // A081 use_throw: with E2A9 (weapons in use) >= 6 the throw is refused — nothing changes.
            if (Projectiles.Count >= Physics.MaxActiveTools)
                return;

            // 9EEE vic_walk: the heading comes from the keys (vic_face) BEFORE use_tool, on the same frame.
            if (input.Horizontal != 0)
                p.Facing = input.Horizontal < 0 ? Facing.Left : Facing.Right;
            p.State = PlayerState.Throwing;
            p.StateTick = 0;
            p.Pose = PlayerPoses.Use(p.Facing);

            // A081: the throw itself plays no sound — the whir is the tool spinning in flight (A87F).
            return;
        }

        // A0A6/A0F0: only starts digging if there is something to dig ahead.
        if (!TryStartDig())
            return;

        p.State = PlayerState.Digging;
        p.StateTick = 0;
        p.Pose = PlayerPoses.Use(p.Facing);
    }

    /// <summary>
    /// A190: one jump tick, in the original's order — (1) Y in 8.8 with the speed <c>E292</c>;
    /// (2) head touching solid sets <c>E2A5</c> (does not brake); (3) walking in the air with ONE tip only,
    /// at mid body (y+8, or y+14 with the head touching); (4) landing at any moment, including
    /// going up, with Y rounded to the cell — that is what lets it climb onto a block by jumping.
    /// </summary>
    private void StepJump()
    {
        var p = Player;
        if (p.VelocityY != Physics.JumpVelocityFixed)
            p.VelocityY += Physics.JumpGravityFixed;
        int y = (p.Y << 8) + p.YFraction + p.VelocityY;
        p.Y = y >> 8;
        p.YFraction = y & 0xFF;
        p.StateTick++;

        bool headHit = HeadTouches(p.X, p.Y);
        if (p.JumpDirection != 0)
        {
            int total = (p.X << 8) + p.XFraction + p.JumpDirection * Physics.JumpDxFixed;
            int newX = total >> 8;
            int probeX = p.JumpDirection < 0 ? newX - 1 : newX + Physics.ActorSize;
            int probeY = p.Y + 8 + (headHit ? 6 : 0);
            if (newX >= GameConstants.PlayfieldWidth - 16 || newX < 0 || !SolidForPlayer(probeX, probeY))
            {
                p.X = newX;
                p.XFraction = total & 0xFF;
            }
            else
            {
                p.X = SnapToCell(newX, p.Facing);
                p.XFraction = 0;
            }
        }

        if (LandsHere(p.X, p.Y))
            LandSnapped();
    }

    /// <summary>A2F0: straight fall of 4 px/tick; lands on any non-empty cell under the feet.</summary>
    private void StepFall()
    {
        var p = Player;
        p.Y += Physics.FallSpeed;
        if (Grounded(p.X, p.Y))
            LandSnapped();
    }

    /// <summary>A446: lands with Y rounded down to the cell (the feet stay on top of the block).</summary>
    private void LandSnapped()
    {
        var p = Player;
        PlaySound(OriginalSounds.PlayerLand);
        p.Y &= ~(GameConstants.CellSize - 1);
        p.YFraction = 0;
        Land();
    }

    /// <summary>
    /// A31C: Vick spins (poses 0..3, 3 ticks each) 5 times, lies down (pose 6) for 0x28 ticks and only then
    /// the life ends.
    /// </summary>
    private void StepDeath()
    {
        var p = Player;
        if (--p.AnimTimer > 0)
            return;

        if (p.AnimPhase == PlayerPoses.DeathSpins)
        {
            // The "lost a life" state (which freezes enemies/projectiles) only comes in at the end of this
            // frame — see _deathEnded in Tick.
            _deathEnded = true;
            return;
        }

        p.AnimTimer = PlayerPoses.DeathFrameTicks;
        if (++p.Pose != PlayerPoses.DeathSpinFrames)
            return;

        if (++p.AnimPhase == PlayerPoses.DeathSpins)
        {
            p.Pose = PlayerPoses.DeathFinalPose;
            p.AnimTimer = PlayerPoses.DeathFinalTicks;
            PlaySound(OriginalSounds.PlayerDeathJingle);
        }
        else
        {
            p.Pose = 0;
        }
    }

    private bool TryStartClimb(int vertical)
    {
        var p = Player;
        int probeY = vertical < 0 ? p.Y + Physics.ActorSize - 1 : p.Y + Physics.ActorSize;

        // Looks for a ladder at multiple points under the body (center, left, right)
        int ladderX = -1;
        foreach (int dx in new[] { Physics.ActorSize / 2, 4, 12, 2, 14 })
        {
            if (Map.IsLadder(p.X + dx, probeY))
            {
                ladderX = p.X + dx;
                break;
            }
        }

        if (ladderX < 0)
            return false;

        int column = RoomMap.FloorDiv(ladderX);
        int row = RoomMap.FloorDiv(probeY);
        int left = Map.Get(column - 1, row) == TerrainKind.Ladder ? column - 1 : column;
        p.X = left * GameConstants.CellSize;
        p.State = PlayerState.Climbing;
        p.Pose = PlayerPoses.Climb;
        p.AnimTimer = 1;
        return true;
    }

    /// <summary>A2A5: on the ladder the pose alternates 6/7 every 4 ticks while going up or down.</summary>
    private void AdvanceClimbPose(PlayerInput input)
    {
        var p = Player;
        if (input.Vertical == 0)
        {
            p.AnimTimer = 1;
            return;
        }

        if (--p.AnimTimer > 0)
            return;

        p.AnimTimer = StepEveryTicks;
        p.Pose ^= 1;
    }

    private void StepClimb(PlayerInput input)
    {
        var p = Player;
        int cx = p.X + Physics.ActorSize / 2;
        int feet = p.Y + Physics.ActorSize;

        if (input.Horizontal != 0 && feet % GameConstants.CellSize == 0 && Grounded(p.X, p.Y))
        {
            p.State = PlayerState.Standing;
            p.AnimTimer = 1;
            StepStanding(input);
            return;
        }

        AdvanceClimbPose(input);

        if (input.Up)
        {
            p.Y -= Physics.ClimbSpeed;
            // A5A2: the ROM probes at y + 0x0A (not y + 0x10). Outside the room what decides is the room
            // change (A67C), not the snap: passing through the ceiling Vick reappears at the bottom of the room
            // above with the head (y+10) outside the room, and the snap threw him back.
            int probe = p.Y + LadderUpProbe;
            if (probe >= 0 && probe < GameConstants.PlayfieldHeight && !Map.IsLadder(cx, probe))
            {
                // Passed the top: stands on top of the ladder (snaps to the cell, as in the original).
                p.Y = (RoomMap.FloorDiv(probe) + 1) * GameConstants.CellSize - Physics.ActorSize;
                p.State = PlayerState.Standing;
            }
        }
        else if (input.Down)
        {
            p.Y += Physics.ClimbSpeed;
            feet = p.Y + Physics.ActorSize;
            bool ladderBelow = Map.IsLadder(cx, feet);
            bool bottomExitLadder = feet >= GameConstants.PlayfieldHeight && Map.IsLadder(cx, GameConstants.PlayfieldHeight - 1);
            if (bottomExitLadder)
            {
                // Stays on the ladder going down through the room's bottom exit until CheckRoomExit is triggered
            }
            else if (!ladderBelow && (Map.IsSolid(p.X + 3, feet) || Map.IsSolid(p.X + 12, feet)))
                p.State = PlayerState.Standing;
            else if (!ladderBelow && !Map.IsLadder(cx, feet - 1))
                p.State = PlayerState.Falling;
        }
    }

    private void TryPushBlock(int dir)
    {
        var p = Player;
        var probe = new Box(p.X + dir, p.Y, Physics.ActorSize, Physics.ActorSize);
        var block = Enemies.FirstOrDefault(e => e.Kind == EnemyKind.MovableBlock && e.Room == CurrentRoom && e.Bounds.Intersects(probe));
        if (block == null || EnemyBlocked(block, dir))
            return;

        MarkBlock(block, false);
        block.X += dir;
        p.X += dir;
        MarkBlock(block, true);
    }

    /// <summary>
    /// Marks/unmarks the 2×2 cells the pushable stone occupies as wall (B9A2: the ROM writes
    /// wall into the stone's cells). Called on birth, on push and on the fall.
    /// </summary>
    private void MarkBlock(Enemy block, bool on)
    {
        var map = Room(block.Room);
        int c = block.X >> 3, r = block.Y >> 3;
        for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
                map.SetRock(c + dx, r + dy, on);
    }

    // ── Player collision ──────────────────────────────────────────────────────────

    private bool SolidForPlayer(int x, int y)
    {
        // When actively pushing through a rotating door, that door's space is open to Vick:
        foreach (var m in RoomMechanisms)
        {
            if (m.Kind is (MechanismKind.RotatingDoor or MechanismKind.Unknown2) && _mechanismStates.TryGetValue(m, out var st) && st.Pushing)
            {
                int mx = m.Position.X, my = m.Position.Y;
                int mw = m.WidthCells * GameConstants.CellSize;
                int mh = m.HeightCells * GameConstants.CellSize;
                if (x >= mx && x < mx + mw && y >= my && y < my + mh)
                    return false;
            }
        }

        if (Map.IsSolid(x, y))
            return true;

        foreach (var e in Enemies)
        {
            if (e.Kind == EnemyKind.MovableBlock && e.Alive && e.Room == CurrentRoom &&
                x >= e.X && x < e.X + Physics.ActorSize && y >= e.Y && y < e.Y + Physics.ActorSize)
                return true;
        }

        return false;
    }

    /// <summary>2-bit value of the cell (98E2): 0 empty, 1 ladder, 2 diggable, 3 wall; block = 3.</summary>
    private int TileForPlayer(int x, int y) => SolidForPlayer(x, y) ? 3 : Map.At(x, y) == TerrainKind.Ladder ? 1 : 0;

    /// <summary>A512: something (including ladder) under the feet at x+3, x+8 or x+13.</summary>
    private bool Grounded(int x, int y) => FeetTile(x, y) != 0;

    private int FeetTile(int x, int y)
    {
        int feet = y + Physics.ActorSize;
        foreach (int dx in FeetProbes)
        {
            int t = TileForPlayer(x + dx, feet);
            if (t != 0)
                return t;
        }

        return 0;
    }

    private static readonly int[] FeetProbes = { 3, 8, 13 };

    /// <summary>A45E: ladder at body height (x+2 or x+14) — standing on it does not fall.</summary>
    private bool LadderAtBody(int x, int y) => TileForPlayer(x + 2, y) == 1 || TileForPlayer(x + 14, y) == 1;

    /// <summary>
    /// A535: lands if there is something solid under the feet; on a ladder, only at its top (no ladder above) or
    /// if there is also solid.
    /// </summary>
    private bool LandsHere(int x, int y)
    {
        int feet = y + Physics.ActorSize;
        int probe = -1, tile = 0;
        foreach (int dx in FeetProbes)
        {
            tile = TileForPlayer(x + dx, feet);
            if (tile != 0)
            {
                probe = x + dx;
                break;
            }
        }

        if (tile == 0)
            return false;
        if (tile != 1)
            return true;

        int row = (feet - 12) & ~(GameConstants.CellSize - 1);
        if (TileForPlayer(probe, row) != 0 || TileForPlayer(probe, row + 8) != 0)
            return FeetProbes.Any(dx => TileForPlayer(x + dx, feet) >= 2);

        return TileForPlayer(probe, row + 16) != 0;
    }

    /// <summary>A1E6: head touching solid (rows y-1, cell+8 and cell+10; x+2 and x+14).</summary>
    private bool HeadTouches(int x, int y)
    {
        int cell = y & ~(GameConstants.CellSize - 1);
        foreach (int row in new[] { y - 1, cell + 8, cell + 10 })
        {
            if (SolidForPlayer(x + 2, row) || SolidForPlayer(x + 14, row))
                return true;
        }

        return false;
    }

    /// <summary>9910 (C=2/3): wall at the target edge at y, y+6 and y+12.</summary>
    private bool WallAhead(int newX, int y, int dir)
    {
        int edge = dir < 0 ? newX - 1 : newX + Physics.ActorSize;
        return SolidForPlayer(edge, y) || SolidForPlayer(edge, y + 6) || SolidForPlayer(edge, y + 12);
    }

    /// <summary>9FC1: hit the wall — X snaps to the cell (going left it rounds up).</summary>
    private static int SnapToCell(int newX, Facing facing) =>
        (newX + (facing == Facing.Left ? GameConstants.CellSize - 1 : 0)) & ~(GameConstants.CellSize - 1);
}
