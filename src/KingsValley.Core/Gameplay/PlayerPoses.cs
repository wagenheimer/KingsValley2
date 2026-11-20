namespace KingsValley.Core.Gameplay;

/// <summary>
/// Vick's pose set. Values 0..6 are the item in hand (<c>E287</c>): the pose page in
/// VRAM is swapped by the game (routine 58DB). <see cref="Death"/> = <c>E298</c> (0x953D, work area
/// 0xE000) and <see cref="Door"/> = <c>E24C</c> (routine 5687 = <c>copy_abb9</c>, <c>rle_abb9</c> →
/// 0xF800).
/// </summary>
public enum PlayerPoseSet
{
    /// <summary>Empty hands.</summary>
    Empty = 0,

    /// <summary>Knife.</summary>
    Knife = 1,

    /// <summary>Boomerang.</summary>
    Boomerang = 2,

    /// <summary>Item 3 (digs down).</summary>
    Item3 = 3,

    /// <summary>Item 4 (digs down).</summary>
    Item4 = 4,

    /// <summary>Item 5 (digs forward).</summary>
    Item5 = 5,

    /// <summary>Item 6 (digs forward).</summary>
    Item6 = 6,

    /// <summary>Fall/death.</summary>
    Death = 7,

    /// <summary>Leaving through the door.</summary>
    Door = 8,
}

/// <summary>
/// Pose indices (<c>E285</c>) read from the original code (bank 2/3 of the MSX2 ROM).
/// Left and right are distinct poses (the MSX does not mirror sprites in hardware).
/// </summary>
public static class PlayerPoses
{
    /// <summary>Walk cycle to the left (9F45), changes every 4 ticks.</summary>
    public static readonly int[] WalkLeft = { 0, 1, 2, 1 };

    /// <summary>Walk cycle to the right (9F49).</summary>
    public static readonly int[] WalkRight = { 3, 4, 5, 4 };

    /// <summary>Idle/falling facing left (9FF2).</summary>
    public const int StandLeft = 1;

    /// <summary>Idle/falling facing right.</summary>
    public const int StandRight = 4;

    /// <summary>Ladder: alternates 6/7 every 4 ticks while moving (A2B2).</summary>
    public const int Climb = 6;

    /// <summary>Jump: 8 (left) or 9 (right) (A02D).</summary>
    public const int JumpLeft = 8;

    /// <summary>Use item: 10/11 (left) or 12/13 (right) (A08D).</summary>
    public const int UseLeft = 10;

    /// <summary>Use item to the right.</summary>
    public const int UseRight = 12;

    /// <summary>Death (set <see cref="PlayerPoseSet.Death"/>): spin 0..3 every 3 ticks.</summary>
    public const int DeathSpinFrames = 4;

    /// <summary>Death: the spin runs 5 times (A31C: D=5).</summary>
    public const int DeathSpins = 5;

    /// <summary>Death: ticks per spin pose.</summary>
    public const int DeathFrameTicks = 3;

    /// <summary>Death: final pose (lying down) and how long it stays in it (0x28 ticks).</summary>
    public const int DeathFinalPose = 6;

    /// <summary>Death final pose ticks.</summary>
    public const int DeathFinalTicks = 0x28;

    /// <summary>
    /// Door entry (set <see cref="PlayerPoseSet.Door"/>): the original builds the raw index
    /// <c>2*E258 + bit4(E259)</c> (<c>7244</c>) — 8 groups of 16 units, order <c>1, 0, 3, 2, 5, 4,
    /// 7, 6</c> — and Vick <b>never</b> vanishes during the entry: routine <c>5687</c> (<c>copy_abb9</c>)
    /// decompresses <c>rle_abb9</c> (0xABB9) into <c>0xF800</c> = 1024 bytes = <b>8 poses</b> of 128
    /// (<c>vick_d_00..07</c>). Measured in openMSX (VRAM dump during the sequence at f100/f162/f235):
    /// it matches byte by byte. The shrinking is in the ART (the color table is fixed: 0x0D/0x4E in the 16 rows).
    /// </summary>
    public const int DoorArtPoses = 8;

    /// <summary>Walk/idle pose for the direction.</summary>
    public static int Walk(Facing facing, int phase) =>
        (facing == Facing.Left ? WalkLeft : WalkRight)[phase & 3];

    /// <summary>Idle pose for the direction.</summary>
    public static int Stand(Facing facing) => facing == Facing.Left ? StandLeft : StandRight;

    /// <summary>First use-item pose for the direction.</summary>
    public static int Use(Facing facing) => facing == Facing.Left ? UseLeft : UseRight;

    /// <summary>Jump pose for the direction.</summary>
    public static int Jump(Facing facing) => JumpLeft + (facing == Facing.Left ? 0 : 1);
}
