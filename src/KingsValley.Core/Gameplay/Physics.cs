namespace KingsValley.Core.Gameplay;

/// <summary>
/// Movement numbers of the original, per logic <b>tick</b>. <b>1 ROM logic call =
/// 1 port tick</b>: the H.TIMI ISR is held by <c>E206</c> while the previous frame has not finished
/// and the effective logic runs at ~30 Hz. Every counter read from the ROM (<c>E286</c>, <c>E296</c>, tool
/// timers) counts <b>1 per call</b> — use the ROM value directly in ticks, without dividing.
/// Measured by reading RAM frame by frame in openMSX (player <c>E280..E28F</c>, enemy <c>E500..</c>)
/// — see <c>docs/ROM_EXTRACTION.md</c> §5. Fractional speeds in 8.8 fixed point (256 = 1 px),
/// as the original stores them (fraction byte before the pixel byte).
/// </summary>
public static class Physics
{
    /// <summary>Size of the player and enemy box.</summary>
    public const int ActorSize = 16;

    // ── Player ─────────────────────────────────────────────────────────────────────

    /// <summary>Walk: 2 px/tick. Measured.</summary>
    public const int WalkSpeed = 2;

    /// <summary>Ladder: 2 px/tick. Measured.</summary>
    public const int ClimbSpeed = 2;

    /// <summary>Player fall: 4 px/tick, constant, with no horizontal displacement. Measured.</summary>
    public const int FallSpeed = 4;

    /// <summary>
    /// Jump (ROM routine A1C1): the vertical speed <c>E292</c> starts at -4 px/tick (0xFC00) and
    /// adds 0x80 per tick (before moving) up to +4 px/tick, in 8.8 fixed point. It gives the 14 px peak and
    /// 15 ticks until it is back on the ground — what the measurement already showed.
    /// </summary>
    public const int JumpVelocityFixed = 0x400;

    /// <summary>Jump gravity: 0x80 (0.5 px/tick²) per tick.</summary>
    public const int JumpGravityFixed = 0x80;

    /// <summary>Horizontal displacement in the jump: 1.25 px/tick (0x140 in 8.8). Measured.</summary>
    public const int JumpDxFixed = 0x140;

    /// <summary>
    /// Throw (A384 <c>vic_throw</c>): <c>E286 = 8</c> logic <b>calls</b> standing still — the
    /// counter is decremented once per call (<c>dec (ix+6)</c> in state 8 of <c>vic_tick</c>,
    /// which runs once per call) = 8 port ticks. The pose (<c>E285</c>) advances when <c>E286 == 4</c>.
    /// </summary>
    public const int ThrowTicks = 8;

    /// <summary>A384: the throw pose advances with <c>E286 == 4</c> (4 ticks).</summary>
    public const int ThrowPoseTick = 4;

    /// <summary>
    /// A38F <c>throw_done</c>: the tool is born <c>0x10</c> px ahead of Vick when the space ahead
    /// is free (<c>VicX ± B</c>, with <c>B = 0x10</c> only if the <c>probe_step</c> probe passes);
    /// otherwise it is born at Vick's own X. Left guard: <c>VicX &lt; 0x10</c> does not even probe.
    /// </summary>
    public const int ThrowDistance = 0x10;

    /// <summary>Digging: the player stands still for 0x20 ticks (A0E9, <c>E286 = 0x20</c>). Read from the ROM.</summary>
    public const int DigTicks = 0x20;

    /// <summary>
    /// Knife step per logic call (A826 <c>tool_step_x</c> adds/subtracts <c>4</c> px —
    /// <c>bc = 0x0402</c>; the <c>RRA</c> of A729 only decides the stop alignment, 4 or 0), with the
    /// wall probe (<c>probe_step</c>) after moving. 1 call = 1 port tick:
    /// <b>4 px per tick</b>.
    /// </summary>
    public const int ProjectileStep = 4;

    /// <summary>A081 <c>use_throw</c>: with <c>E2A9 &gt;= 6</c> weapons in use the throw is refused.</summary>
    public const int MaxActiveTools = 6;

    /// <summary>A38F <c>throw_done</c>: with <c>VicX &gt;= 0xE8</c> (right) it does not even probe — X+0x10 would leave the screen.</summary>
    public const int ThrowProbeMaxX = 0xE8;

    /// <summary>A38F <c>throw_done</c>: with <c>VicX &lt; 0x10</c> (left) it does not even probe — X-0x10 would leave the screen.</summary>
    public const int ThrowProbeMinX = 0x10;

    /// <summary>
    /// A79F <c>tool_probe</c>: when the tool itself is already born inside the block the X aligns by
    /// 0x10 px (<c>B = 0x10</c>); when only the tip touches, by 8 px (<c>B = 8</c>).
    /// </summary>
    public const int ToolAlignFar = 0x10;

    /// <summary>A79F: short step of the <c>tool_probe</c> alignment (only the tip touching).</summary>
    public const int ToolAlignNear = 8;

    /// <summary>A729/A8F5: the stopped X snaps to the cell (<c>&amp; 0xF8</c>) and does not go past 0xF0.</summary>
    public const int ToolAlignMaxX = 0xF0;

    /// <summary>
    /// 995A/A9BA: the tool arithmetic stops at <c>0xB8</c> — not at <c>0xC0</c>, which is the
    /// field height. It is the same limit as the step probes (the 16 px sprite fits up to 0xB7).
    /// </summary>
    public const int ToolBottomY = 0xB8;

    /// <summary>
    /// A814 <c>tool_hold</c>: after hitting (wall or enemy) the tool keeps spinning in place
    /// for <c>0x18</c> calls (<c>ld (ix+6),0x18</c>, with a <c>DEC (IX+6)</c> per call) =
    /// <c>0x18</c> port ticks.
    /// </summary>
    public const int ToolHoldTicks = 0x18;

    /// <summary>
    /// A743/A975: when the wait ends the tool already enters the fall with the counter at 4 — it is the
    /// same <c>IX+6</c> that <c>tool_fall</c> reads, so the first <c>DEC</c> does not wrap around
    /// 255 and step 0 (Y+1 per tick) starts from there.
    /// </summary>
    public const int ToolFallStartTicks = 4;

    /// <summary>
    /// A9BA <c>tool_fall</c>: the fall accelerates per step — the timer reloads from the <c>fall_dt</c>
    /// table (<c>DEC (IX+6)</c> per call) and Y moves <c>step + 1</c> px per tick (step 5 has 0xFF:
    /// the fall ends earlier).
    /// </summary>
    public static readonly int[] ToolFallTicks = { 3, 3, 3, 3, 3, 0xFF };

    /// <summary>
    /// Boomerang (A8C7): the original has no "range" — it has a 10-step profile, with the speed
    /// of each step in table <c>A961</c> (<c>boom_dx</c>) and the duration in <c>A96B</c> (<c>boom_dt</c>),
    /// both counted per logic call (<c>DEC (IX+6)</c>) = port ticks. The speed changes
    /// sign on step 5, so the object <b>turns by itself</b> and comes back along the same axis.
    /// </summary>
    public static readonly int[] BoomerangStepSpeed = { 4, 3, 2, 1, 0, -1, -2, -3, -4, -5 };

    /// <summary>Duration in ticks of each boomerang step (table A96B).</summary>
    public static readonly int[] BoomerangStepTicks = { 8, 6, 4, 3, 2, 3, 4, 6, 8, 0xFF };

    /// <summary>
    /// A8E0 <c>boom_vic</c>: Vick catches the boomerang back when it gets closer than 0x0D px —
    /// <c>|VicY - Y|</c> and <c>|VicX - X|</c> smaller than this (holds on every free flight step).
    /// </summary>
    public const int BoomerangCatchRange = 0x0D;

    // Enemies: the constants live together with the ROM-ported AI (StageSession.Mummy/GoldMummy/Guard/Rock).
}
