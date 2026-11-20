using KingsValley.Core.Stages;

namespace KingsValley.Core.Gameplay;

/// <summary>
/// Enemy sprite frame (byte <c>IX+0x11</c> of structure <c>E500</c>), with the rules read
/// from the original code (bank 3 of the MSX2 ROM). The frame indexes the pattern-pair table 0xBEED
/// (atlas <c>enemy_t{type}_{frame}</c>).
/// </summary>
public static class EnemyFrames
{
    /// <summary>Invisible (the original writes 0xFF while the mummy is still inside the sarcophagus).</summary>
    public const int Hidden = -1;

    /// <summary>Facing front (idle/turning, AD98).</summary>
    public const int Front = 0;

    /// <summary>Death effect colors (0x0A + 0x4B, 6624): atlas <c>enemy_t5_*</c>.</summary>
    public const int VanishColorType = 5;

    /// <summary>Enemy frame: <c>IX+0x11</c> itself, kept by the ROM-ported AI.</summary>
    public static int For(Enemy e, int tick) => e.Frame;

    /// <summary>
    /// The types use the same colors on the MSX2, except type 2 (AD0B) and type 3 appearing as
    /// a statue (B2DB), which use type 2's colors.
    /// </summary>
    public static int ColorType(Enemy e) =>
        e.Vanishing ? VanishColorType :
        e.Kind == EnemyKind.Guard && e.Yellow ? (int)EnemyKind.GoldMummy :
        e.Kind == EnemyKind.Rock && e.Yellow ? RockColorType : (int)e.Kind;

    /// <summary>Stone after appearing: colors 0x07/0x49 (B6CB) — atlas <c>enemy_t6_*</c>.</summary>
    public const int RockColorType = 6;
}
