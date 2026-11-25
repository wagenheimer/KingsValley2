namespace KingsValley.Core.Gameplay;

/// <summary>
/// The ROM's **pyramid FX** table (<c>0xB548</c>, bank 12) read by <c>load_ef10</c> (0xB400).
///
/// <para><b>5-byte</b> records terminated by <c>0</c>: <c>[stage, EF10, type, x, y]</c>. The loader
/// walks the table comparing <c>E242</c> (stage) and, when it matches, copies the rest into
/// <c>EF10..EF14</c>; it does <b>not zero</b> when there is no match — so the value <b>persists</b> from the last stage
/// with a record.</para>
///
/// <para>This matters for the end of the game: stage 60 has no record, and the last one is stage 55's
/// (<c>EF10 = 2</c>). The <c>end_menu</c> (0xB53A) picks the page by <c>EF10</c>: <b>1</b> = sound
/// select (<c>0xB917</c>), <b>otherwise</b> = puzzle (<c>0xB6B0</c>); and the <c>end_text</c> (0x4B9C) prints
/// <c>MUSIC STAGE</c> or <c>PUZZLE STAGE</c> by the same index. After stage 60 the player lands,
/// therefore, in the <b>puzzle</b>.</para>
/// </summary>
public static class StageFx
{
    /// <summary>Registros de <c>0xB548</c> (fim em zero).</summary>
    private static readonly (int Stage, int Ef10, int Kind, int X, int Y)[] Records =
    [
        (7, 1, 2, 8, 8),
        (20, 2, 2, 136, 48),
        (24, 1, 4, 40, 224),
        (29, 2, 1, 168, 224),
        (32, 1, 3, 128, 48),
        (35, 2, 3, 24, 216),
        (40, 1, 1, 128, 112),
        (43, 2, 1, 160, 152),
        (50, 1, 2, 168, 24),
        (52, 2, 1, 168, 224),
        (53, 1, 3, 56, 176),
        (55, 2, 2, 120, 192),
    ];

    /// <summary>
    /// <c>EF10</c> in force for the requested stage: the exact record's, or the last previous record's
    /// (the loader does not zero). Returns 0 before the first record.
    /// </summary>
    public static int Ef10At(int stage)
    {
        int value = 0;
        foreach (var record in Records)
        {
            if (record.Stage > stage)
                break;
            value = record.Ef10;
        }

        return value;
    }

    /// <summary>Exact record for the stage, if it exists (for the module's visual FX <c>0xB51C</c>).</summary>
    public static (int Ef10, int Kind, int X, int Y)? RecordOf(int stage)
    {
        foreach (var record in Records)
            if (record.Stage == stage)
                return (record.Ef10, record.Kind, record.X, record.Y);
        return null;
    }

    /// <summary><c>end_menu</c> opens the sound select only when <c>EF10 == 1</c>; otherwise, the puzzle.</summary>
    public static bool OpensSoundSelect(int stage) => Ef10At(stage) == 1;
}
