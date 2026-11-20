using KingsValley.Core.Common;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// Enemy loop, pushable block, contact and projectiles. The AI of each type is ported from the ROM in
/// <c>StageSession.Mummy.cs</c> (1), <c>.GoldMummy.cs</c> (2), <c>.Guard.cs</c> (3) and <c>.Rock.cs</c> (4).
/// </summary>
public sealed partial class StageSession
{
    /// <summary>65F6/654C: each enemy runs the AI of its type and the integrator, in any room.</summary>
    private void UpdateEnemies()
    {
        foreach (var e in Enemies.ToList())
        {
            if (!Enemies.Contains(e))
                continue;

            e.Tick++;
            if (e.Kind == EnemyKind.MovableBlock)
            {
                if (e.Room == CurrentRoom)
                    FallBlock(e);
                continue;
            }

            UpdateOriginalEnemy(e);
        }
    }

    private void FallBlock(Enemy e)
    {
        MarkBlock(e, false);
        for (int i = 0; i < Physics.FallSpeed && !EnemyGrounded(e); i++)
            e.Y++;
        MarkBlock(e, true);
    }

    private bool EnemyGrounded(Enemy e)
    {
        int feet = e.Y + Physics.ActorSize;
        return Map.IsSolid(e.X + 3, feet) || Map.IsSolid(e.X + 12, feet)
            || Map.IsLadderTop(e.X + 3, feet) || Map.IsLadderTop(e.X + 12, feet)
            || Enemies.Any(o => o != e && o.Kind == EnemyKind.MovableBlock && o.Room == e.Room
                && feet == o.Y && e.X + 12 > o.X && e.X + 3 < o.X + Physics.ActorSize);
    }

    private bool EnemyBlocked(Enemy e, int dx)
    {
        int edge = dx > 0 ? e.X + Physics.ActorSize - 1 + dx : e.X + dx;
        if (edge < 0 || edge >= GameConstants.PlayfieldWidth)
            return Map.IsSolid(Math.Clamp(edge, 0, GameConstants.PlayfieldWidth - 1), e.Y + 8);

        return Map.IsSolid(edge, e.Y) || Map.IsSolid(edge, e.Y + 8) || Map.IsSolid(edge, e.Y + Physics.ActorSize - 1);
    }

    /// <summary>
    /// 9AB1: the touch kills if the enemy is dangerous (bit 0 of <c>IX+0x13</c>) and inside the original
    /// box: (y+8) - ey in [0,16) and (x+10) - ex in [0,20). There is no ladder rule.
    /// </summary>
    private void CheckEnemyContact()
    {
        if (Player.State is PlayerState.Dying or PlayerState.Exiting)
            return;

        foreach (var e in Enemies)
        {
            if (e.Room != CurrentRoom || !e.Kind.IsHostile() || !IsDangerous(e))
                continue;

            int dy = Player.Y + 8 - e.Y, dx = Player.X + 10 - e.X;
            if (dy is >= 0 and < 16 && dx is >= 0 and < 20)
            {
                Die();
                return;
            }
        }
    }

    private static bool IsDangerous(Enemy e) => e.Dangerous;

    private static bool IsShootable(Enemy e) => e.Shootable;

    /// <summary>
    /// AFB4 (called at <c>4AFB</c> when the room transition ends, that is, with <c>E24D=0</c>):
    /// every enemy that <b>kills on touch</b> (bit 0 of <c>IX+0x13</c>) and is in the player's room
    /// inside the 48x48 box centered on him (<c>(ey-y)+0x18 &lt; 0x30</c>, same for x) is removed
    /// as if it had been hit (<c>AFF6</c> → <c>6706</c>), with no points and no sound.
    ///
    /// It is the original's protection: when entering a room (or at the start of the stage) Vick does not die on
    /// the first frame because of an enemy that was already on top of the arrival position.
    /// </summary>
    private void ClearDangerousEnemiesNearPlayer()
    {
        foreach (var e in Enemies.ToList())
        {
            if (!e.Kind.IsHostile() || !IsDangerous(e) || e.Room != CurrentRoom)
                continue;

            // 24 px to each side: AFF6 adds 0x18 and compares with 0x30 (the 8-bit wrap is symmetric).
            int dy = e.Y - Player.Y, dx = e.X - Player.X;
            if (dy is < -24 or >= 24 || dx is < -24 or >= 24)
                continue;

            Vanish(e, withSound: false);
        }
    }

    // ── Projectiles ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A38F <c>throw_done</c> (end of <c>vic_throw</c>, A384): Vick is left with empty hands, goes back to
    /// the idle pose and the tool is born in phase <see cref="ProjectilePhase.Go"/> with
    /// <c>Y = VicY &amp; 0xF8</c> and <c>X = VicX ± 0x10</c> — the ±0x10 only when the
    /// <c>probe_step</c> probe finds the space ahead free (ROM guards: on the right it does not even probe if
    /// <c>VicX ≥ 0xE8</c>, on the left if <c>VicX &lt; 0x10</c>).
    /// </summary>
    private void FinishThrow()
    {
        var p = Player;
        var kind = p.Holding;
        p.Holding = ItemKind.None;
        p.State = PlayerState.Standing;
        p.Pose = PlayerPoses.Walk(p.Facing, 0);

        bool right = p.Facing == Facing.Right;
        int x = p.X;
        if (right ? p.X < Physics.ThrowProbeMaxX : p.X >= Physics.ThrowProbeMinX)
        {
            if (!WallAhead(p.X, p.Y, (int)p.Facing))
                x = p.X + (right ? Physics.ThrowDistance : -Physics.ThrowDistance);
        }

        Projectiles.Add(new Projectile
        {
            Kind = kind,
            X = x,
            Y = p.Y & ~(GameConstants.CellSize - 1),
            Facing = p.Facing,
            Room = CurrentRoom,   // A6FF tool_scr: ix+3 = E243 (the room of the throw)
        });
    }

    /// <summary>
    /// A6BD/A89B <c>tick_map_tools</c>: each tool runs the handler of its phase (A6DD/A8A1
    /// dispatch by <c>phase - 1</c>) <b>once per logic call</b> — <c>play_tick</c> runs
    /// once per call (together with <c>tick_enemies</c>), so it is 1 step per port tick. The animation
    /// (A87F/A99D) only advances when <c>E203 &amp; 1 == 0</c>, that is every 2 calls. The collision with
    /// the enemies (9AFE <c>e300_enemy_hit</c>) is the <b>last</b> step of <c>play_tick</c> — see
    /// <see cref="CheckToolHits"/>.
    /// </summary>
    private void UpdateProjectiles()
    {
        bool anim = (TickCount & 1) == 0;   // A87F/A99D: E203 & 1 == 0 (every 2 calls)
        for (int i = Projectiles.Count - 1; i >= 0; i--)
            StepToolFrame(Projectiles[i], anim);
    }

    /// <summary>
    /// 9AFE <c>e300_enemy_hit</c>: the last step of <c>play_tick</c> (after <c>tick_map_tools</c>,
    /// <c>tick_enemies</c> and <c>vic_enemy_overlap</c>) — the hit test sees the positions already
    /// moved on this tick.
    /// </summary>
    private void CheckToolHits()
    {
        for (int i = Projectiles.Count - 1; i >= 0; i--)
            HitEnemies(Projectiles[i]);
    }

    /// <summary>One ROM logic call: A6F2/A8A1 dispatch by phase (<c>tool_phase</c> - 1).</summary>
    private void StepToolFrame(Projectile pr, bool anim)
    {
        switch (pr.Phase)
        {
            case ProjectilePhase.Go:
                GoToolFrame(pr, anim);
                break;
            case ProjectilePhase.Flying:
                FlyToolFrame(pr, anim);
                break;
            case ProjectilePhase.Waiting:
                WaitToolFrame(pr, anim);
                break;
            default:
                FallToolFrame(pr, anim);
                break;
        }
    }

    /// <summary>
    /// A706/A8AE <c>knife_go</c>/<c>boom_go</c> (phase 2, one call): initializes the tool, moves
    /// to phase 3 (<c>tool_next</c>) and probes ahead (A79F <c>tool_probe</c>). Free, it <b>already flies
    /// on this call</b> (the ROM falls straight into <c>knife_fly</c>/<c>boom_fly</c>); touching, it is born
    /// spinning in place with the <c>tool_hold</c> of 0x18 ticks (phase 4).
    /// </summary>
    private void GoToolFrame(Projectile pr, bool anim)
    {
        if (pr.Kind == ItemKind.Boomerang)
        {
            pr.Anim = 3;
            pr.Step = 0;
            pr.Timer = Physics.BoomerangStepTicks[0];
        }
        else
        {
            pr.Anim = 0;
        }

        pr.Phase = ProjectilePhase.Flying;
        if (ToolProbe(pr))
            FlyToolFrame(pr, anim);
    }

    /// <summary>A717/A8C7: animates (when <c>E203 &amp; 1 == 0</c>) and runs the tool flight of its type.</summary>
    private void FlyToolFrame(Projectile pr, bool anim)
    {
        if (anim)
            AdvanceToolAnim(pr);

        if (pr.Kind == ItemKind.Boomerang)
            FlyBoomerang(pr);
        else
            FlyKnife(pr);
    }

    /// <summary>
    /// A717 <c>knife_fly</c>: moves 4 px in the throw direction (A826 <c>tool_step_x</c>, with the probe
    /// at the edge). With the hit flagged (A787 <c>tool_lock</c>) the step is skipped and the alignment uses the
    /// heading — on stopping, the X snaps to the cell and the knife spins 0x18 ticks before falling.
    /// </summary>
    private void FlyKnife(Projectile pr)
    {
        bool right = pr.Facing == Facing.Right;
        int dx = right ? Physics.ProjectileStep : -Physics.ProjectileStep;
        if (pr.HitLocked || !MoveToolX(pr, dx))
            AlignAndHold(pr, right);
    }

    /// <summary>
    /// A8C7 <c>boom_fly</c>: the step moves by the A961/A96B profile — the speed changes sign on
    /// step 5, so it <b>turns by itself</b> and comes back along the same axis. With empty hands Vick catches
    /// it back if he is closer than 0x0D px (<c>boom_vic</c>, on every free step); on hitting (wall
    /// or flagged hit) it stops, snaps to the cell and spins 0x18 ticks before falling.
    /// </summary>
    private void FlyBoomerang(Projectile pr)
    {
        if (pr.HitLocked)
        {
            AlignAndHold(pr, pr.Facing == Facing.Right);
            return;
        }

        pr.Timer--;
        if (pr.Timer <= 0)
        {
            pr.Step = Math.Min(pr.Step + 1, Physics.BoomerangStepSpeed.Length - 1);
            pr.Timer = Physics.BoomerangStepTicks[pr.Step];
        }

        int dx = Physics.BoomerangStepSpeed[pr.Step];
        if (pr.Facing == Facing.Left)
            dx = -dx;

        if (!MoveToolX(pr, dx))
        {
            AlignAndHold(pr, dx >= 0);
            return;
        }

        TryCatchBoomerang(pr);
    }

    /// <summary>
    /// A743/A975 <c>knife_wait</c>/<c>boom_wait</c> (phase 4): spins in place until the counter reaches zero and
    /// then moves to the fall (phase 5) already with the timer at <see cref="Physics.ToolFallStartTicks"/> (the
    /// <c>tool_fall</c> reads the same counter). The boomerang resets the animation frame to 3. If the
    /// tool is already resting on the ground (AA17 <c>tool_sat</c>) it lands at once — and the knife plays
    /// 0x36 (A76E <c>knife_sfx</c>).
    /// </summary>
    private void WaitToolFrame(Projectile pr, bool anim)
    {
        if (anim)
            AdvanceToolAnim(pr);

        pr.Timer--;
        if (pr.Timer > 0)
            return;

        if (pr.Kind == ItemKind.Boomerang)
            pr.Anim = 3;
        pr.Step = 0;
        pr.Timer = Physics.ToolFallStartTicks;
        pr.Facing = Facing.Left;
        pr.Phase = ProjectilePhase.Falling;
        // 0xA743: the ground probe (AA17 tool_sat) only holds in Vick's room.
        if (pr.Room == CurrentRoom && ToolRestsOnFloor(pr))
            LandTool(pr);
    }

    /// <summary>
    /// A9BA <c>tool_fall</c> (phase 5): gravity accelerates per step (<c>DEC (IX+6)</c>; at zero
    /// <c>step = min(step + 1, 5)</c> and the timer reloads from the <c>fall_dt</c> table) and Y moves
    /// <c>step + 1</c> px per tick. Lands when <c>Y ≥ 0xB8</c> (the ROM's <c>tool_wrap_y</c>
    /// would go down to the room below — here the bottom of the field) or when the ground appears at the bottom
    /// center (A9E0: <c>(X + 8, min(Y + 16, 0xB8))</c>).
    /// </summary>
    private void FallToolFrame(Projectile pr, bool anim)
    {
        if (anim)
            AdvanceToolAnim(pr);

        pr.Timer--;
        if (pr.Timer <= 0)
        {
            pr.Step = Math.Min(pr.Step + 1, Physics.ToolFallTicks.Length - 1);
            pr.Timer = Physics.ToolFallTicks[pr.Step];
        }

        pr.Y += pr.Step + 1;
        if (pr.Y >= Physics.ToolBottomY)
        {
            // 0xAA17 tool_wrap_y: Y = 8 and the tool falls into the room BELOW (room_link with A=2) —
            // this is how the knife thrown into a hole continues into the next room.
            int below = RoomGridNavigator.Neighbor(Definition.RoomGrid, pr.Room, 0, 1);
            if (below != pr.Room)
            {
                pr.Room = below;
                pr.Y = 8;
                return;
            }

            pr.Y = Physics.ToolBottomY;
            LandTool(pr);
            return;
        }

        // 0xA9BA: in ANOTHER room the original skips the ground probe (it does not have that room's map).
        if (pr.Room == CurrentRoom &&
            Map.IsSolid(pr.X + 8, Math.Min(pr.Y + Physics.ActorSize, Physics.ToolBottomY)))
            LandTool(pr);
    }

    /// <summary>
    /// AA17 <c>tool_sat</c>: probes the ground under the stopped tool — 3 points at <c>Y + 16</c> saturated
    /// at 0xB8 (<c>X</c>, <c>X + 6</c> and <c>X + 12</c>: the <c>probe_step</c> with C = 1, downwards).
    /// </summary>
    private bool ToolRestsOnFloor(Projectile pr)
    {
        int floor = Math.Min(pr.Y + Physics.ActorSize, Physics.ToolBottomY);
        return Map.IsSolid(pr.X, floor) || Map.IsSolid(pr.X + 6, floor)
            || Map.IsSolid(pr.X + 12, floor);
    }

    /// <summary>
    /// AA3C <c>tool_land</c>: the tool lands — leaves the count of weapons in use (<c>E2A9--</c>), the Y
    /// snaps to the cell (<c>&amp; 0xF8</c>), the phase goes back to "available" and the drawing becomes the
    /// map tiles (2x2 in the ROM; here a <see cref="FloorItem"/>). The knife plays 0x36 only when it lands in THIS
    /// room (A76E/A9E0 <c>knife_sfx</c>); the boomerang has no landing sound.
    /// </summary>
    private void LandTool(Projectile pr)
    {
        pr.Y &= ~(GameConstants.CellSize - 1);
        Items.Add(new FloorItem { Kind = pr.Kind, Room = pr.Room, X = pr.X, Y = pr.Y });
        Projectiles.Remove(pr);
        // A76E/A9E0: 0x36 only plays if it landed in the room Vick is in (E243 == ix+3).
        if (pr.Kind == ItemKind.Knife && pr.Room == CurrentRoom)
            Emit(GameEventKind.ProjectileStopped, pr.X, pr.Y, pr.Kind, OriginalSounds.KnifeStop);
    }

    /// <summary>
    /// A826/A819 <c>tool_step_x</c>/<c>tool_add_x</c>: adds the step to X and probes the attack edge
    /// (9910 <c>probe_step</c>: 3 points at the edge, the same as Vick's steps). Returns <c>true</c>
    /// when the step is free (the ROM's CY returns "free"); the probe runs <b>after</b> moving,
    /// as in the original.
    /// <para>
    /// 0xA826 <c>step_wrap</c>: X is a <b>byte</b> — crossing 0/0xFF the tool changes room
    /// (<c>screen_slot</c> + <c>room_link</c>) and keeps flying; and when it is in a room different
    /// from Vick's the original <b>does not probe</b> (it does not have that map) — it only moves.
    /// </para>
    /// </summary>
    private bool MoveToolX(Projectile pr, int dx)
    {
        pr.X += dx;
        if (pr.X is < 0 or > 0xFF)
        {
            pr.X &= 0xFF;
            pr.Room = RoomGridNavigator.Neighbor(Definition.RoomGrid, pr.Room, dx >= 0 ? 1 : -1, 0);
            return true;
        }

        if (pr.Room != CurrentRoom)
            return true;

        return ToolStepFree(pr, dx >= 0);
    }

    /// <summary>
    /// 9910 <c>probe_step</c> at the tool edge: 3 points at the attack edge — at <c>Y</c>,
    /// <c>Y + 6</c> and <c>Y + 12</c>. The edge is <c>X + 16</c> going right (saturated at 0xF8,
    /// like <c>step_right</c>) and <c>X - 1</c> going left (saturated at 0). The <c>step_wrap</c>
    /// (the room change when X overflows the byte) is handled in <see cref="MoveToolX"/>; here only the
    /// current room is probed.
    /// </summary>
    private bool ToolStepFree(Projectile pr, bool right)
    {
        int edge = right
            ? Math.Min(pr.X + Physics.ActorSize, GameConstants.PlayfieldWidth - 8)
            : Math.Max(pr.X - 1, 0);
        for (int dy = 0; dy <= 12; dy += 6)
        {
            if (Map.IsSolid(edge, pr.Y + dy))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A79F <c>tool_probe</c>: probes the newly born tool at 4 points of the room map. If the
    /// center itself (<c>X + 8</c>, saturated at <c>0xE8</c> like the ROM's <c>cp 0xEE</c>) is already
    /// inside the block — at <c>Y</c> or <c>Y + 8</c> — the snap is by 0x10 (<c>B = 0x10</c>); if it is
    /// only the tip touching (<c>center ± 8</c>, at <c>Y</c> and at <c>Y + 9</c>), by 8. In both
    /// cases it is born spinning in place (<c>tool_hold</c>) instead of flying. Free it returns <c>true</c>
    /// (the ROM's CY).
    /// </summary>
    private bool ToolProbe(Projectile pr)
    {
        bool right = pr.Facing == Facing.Right;
        int cx = pr.X + 8;
        if (cx >= 0xEE)
            cx = 0xE8;

        if (Map.IsSolid(cx, pr.Y) || Map.IsSolid(cx, pr.Y + 8))
        {
            AlignSpawningTool(pr, right, Physics.ToolAlignFar);
            return false;
        }

        int edge = right ? cx + 8 : cx - 8;
        if (Map.IsSolid(edge, pr.Y) || Map.IsSolid(edge, pr.Y + 9))
        {
            AlignSpawningTool(pr, right, Physics.ToolAlignNear);
            return false;
        }

        return true;
    }

    /// <summary>
    /// A79F <c>probe_face</c>: the snap for when the tool is born touching/inside a block —
    /// going right X goes down <c>8 - B</c>, going left it goes up <c>B</c> (B = 0x10 or 8),
    /// and the result snaps to the cell (<c>&amp; 0xF8</c>). It goes straight into the 0x18-tick spin
    /// (<c>tool_next</c> + <c>tool_hold</c>, phase 4).
    /// </summary>
    private static void AlignSpawningTool(Projectile pr, bool right, int step)
    {
        pr.X = Math.Max(0, (pr.X + (right ? 8 - step : step)) & ~(GameConstants.CellSize - 1));
        pr.Timer = Physics.ToolHoldTicks;
        pr.Phase = ProjectilePhase.Waiting;
    }

    /// <summary>
    /// A729/A8F5 <c>knife_align</c>/<c>boom_align</c>: on stopping (wall or flagged hit), the X
    /// snaps to the cell (<c>&amp; 0xF8</c>, going up 4 px when moving left) and does not go past
    /// 0xF0 (<c>cp 0xF1</c>); the phase becomes the wait (<c>tool_hold</c> = 0x18 ticks) and then it falls.
    /// </summary>
    private static void AlignAndHold(Projectile pr, bool right)
    {
        pr.X = Math.Max(0, Math.Min((pr.X + (right ? 0 : 4)) & ~(GameConstants.CellSize - 1),
            Physics.ToolAlignMaxX));
        pr.Timer = Physics.ToolHoldTicks;
        pr.Phase = ProjectilePhase.Waiting;
    }

    /// <summary>
    /// A8E0 <c>boom_vic</c>: with empty hands Vick catches the boomerang back when it passes closer than
    /// 0x0D px (<c>|VicY - Y|</c> and <c>|VicX - X|</c>) — it holds on <b>every</b> free step of
    /// the flight, not only on the way back. Plays 0x35 and the weapon goes back to the hand.
    /// </summary>
    private void TryCatchBoomerang(Projectile pr)
    {
        if (pr.Room != CurrentRoom)   // 0xA97C: only in Vick's room
            return;
        if (Player.Holding != ItemKind.None)
            return;
        if (Math.Abs(Player.Y - pr.Y) >= Physics.BoomerangCatchRange ||
            Math.Abs(Player.X - pr.X) >= Physics.BoomerangCatchRange)
            return;

        Player.Holding = ItemKind.Boomerang;
        Projectiles.Remove(pr);
        PlaySound(OriginalSounds.BoomerangCatch);
    }

    /// <summary>
    /// A87F/A99D: the knife spins 0..2 and the boomerang 3..5 — <b>one step every 2 calls</b> (in the ROM
    /// only when <c>E203 &amp; 1 == 0</c>). Completing the cycle plays the whir (1C for the knife, 1B for the
    /// boomerang) only when it is in Vick's room (<c>E243 == IX+3</c>).
    /// </summary>
    private void AdvanceToolAnim(Projectile pr)
    {
        bool audible = pr.Room == CurrentRoom;
        if (pr.Kind == ItemKind.Boomerang)
        {
            pr.Anim = pr.Anim < 5 ? pr.Anim + 1 : 3;
            if (pr.Anim == 3 && audible)
                PlaySound(OriginalSounds.BoomerangSpin);
        }
        else
        {
            pr.Anim = pr.Anim < 2 ? pr.Anim + 1 : 0;
            if (pr.Anim == 0 && audible)
                PlaySound(OriginalSounds.KnifeSpin);
        }
    }

    /// <summary>
    /// 9AFE <c>e300_enemy_hit</c> (runs at the end of <c>play_tick</c>, <b>after</b>
    /// <c>tick_map_tools</c> and <c>tick_enemies</c>): hits if <c>(toolY + 0x0C) - enemyY</c> is in
    /// <c>[0, 0x18)</c> and <c>|enemyX - toolX| &lt; 0x0C</c>; the enemy turns into sparks (+100, sound 0x26) and the
    /// tool is flagged (<c>EDCD</c>/<c>E2E8</c>) — on the next flight frame the <c>tool_lock</c>
    /// drops it. The stone (type 4) only stops and turns into a block (9B80 → B8C5, with the same points and sound).
    /// </summary>
    private void HitEnemies(Projectile pr)
    {
        foreach (var e in Enemies.ToList())
        {
            if (e.Room != pr.Room || !e.Kind.IsHostile() || !IsShootable(e))
                continue;

            int dy = pr.Y + 12 - e.Y;
            if (dy is < 0 or >= 24 || Math.Abs(e.X - pr.X) >= 12)
                continue;

            if (e.Kind == EnemyKind.Rock)
                RockHit(e);          // 9B80 → B8C5: the stone only stops and turns into a block
            else
                Vanish(e, withSound: false);
            AddScore(Scoring.Enemy);
            Emit(GameEventKind.EnemyKilled, e.X, e.Y, sound: OriginalSounds.EnemyHit);
            pr.HitLocked = true;
        }
    }

}
