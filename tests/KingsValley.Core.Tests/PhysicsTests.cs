using KingsValley.Core.Gameplay;

namespace KingsValley.Core.Tests;

/// <summary>
/// Movement constants read from the ROM (not estimated). The weapons come from <c>tick_map_tools</c>
/// (<c>banks_123.asm</c>): each tool runs <b>1 step per logic call</b> — and 1 ROM
/// call = 1 port tick (the H.TIMI ISR is held at <c>E206</c>; the effective logic runs at ~30 Hz). The knife
/// moves 4 px per call (A826 <c>tool_step_x</c>) and the boomerang follows the 10-step profile of
/// tables <c>A961</c> (speed per step, <c>boom_dx</c>) and <c>A96B</c> (duration in ticks,
/// <c>boom_dt</c>).
/// </summary>
public sealed class PhysicsTests
{
    [Fact]
    public void Boomerang_Profile_IsTheRomTables()
    {
        Assert.Equal(new[] { 4, 3, 2, 1, 0, -1, -2, -3, -4, -5 }, Physics.BoomerangStepSpeed);
        Assert.Equal(new[] { 8, 6, 4, 3, 2, 3, 4, 6, 8, 0xFF }, Physics.BoomerangStepTicks);
    }

    [Fact]
    public void Boomerang_Outbound_AndReturn_EachSum61Pixels()
    {
        // 4x8 + 3x6 + 2x4 + 1x3 + 0x2 = 61 px going out; the way back (steps 5..8) mirrors the same 61 px.
        // Step 9 (0xFF ticks at -5) is the "go until it hits" — it does not close a cycle.
        int outbound = 0;
        for (int step = 0; step < 5; step++)
            outbound += Physics.BoomerangStepSpeed[step] * Physics.BoomerangStepTicks[step];

        int back = 0;
        for (int step = 5; step < 9; step++)
            back -= Physics.BoomerangStepSpeed[step] * Physics.BoomerangStepTicks[step];

        Assert.Equal(61, outbound);
        Assert.Equal(outbound, back);
    }

    [Fact]
    public void Knife_WalksFourPixelsPerTick()
    {
        Assert.Equal(4, Physics.ProjectileStep);   // A826 tool_step_x: bc = 0x0402 (4 px per call)
    }

    [Fact]
    public void Throw_Windup_IsEightCalls()
    {
        Assert.Equal(8, Physics.ThrowTicks);       // A384 vic_throw: E286 = 8, 1 DEC per call
        Assert.Equal(4, Physics.ThrowPoseTick);    // the pose advances with E286 == 4
    }

    [Fact]
    public void Tool_Constants_MatchTheRom()
    {
        Assert.Equal(6, Physics.MaxActiveTools);                            // A081 use_throw: E2A9 >= 6 recusa
        Assert.Equal(0x10, Physics.ThrowDistance);                          // A38F throw_done
        Assert.Equal(0x10, Physics.ToolAlignFar);                           // A79F probe_face (B = 0x10)
        Assert.Equal(8, Physics.ToolAlignNear);                             // A79F probe_face (B = 8)
        Assert.Equal(0xF0, Physics.ToolAlignMaxX);                          // A729/A8F5: cp 0xF1 → 0xF0
        Assert.Equal(0x18, Physics.ToolHoldTicks);                          // A814 tool_hold (DEC (IX+6) per call)
        Assert.Equal(4, Physics.ToolFallStartTicks);                        // A743/A975: enters the fall with 4
        Assert.Equal(new[] { 3, 3, 3, 3, 3, 0xFF }, Physics.ToolFallTicks); // A9BA fall_dt
        Assert.Equal(0x0D, Physics.BoomerangCatchRange);                    // A8E0 boom_vic
        Assert.Equal(0xB8, Physics.ToolBottomY);                            // A9BA/995A
    }
}
