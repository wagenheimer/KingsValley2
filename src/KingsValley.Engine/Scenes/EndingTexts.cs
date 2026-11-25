#nullable enable
namespace KingsValley.Engine.Scenes;

/// <summary>
/// Ending texts of the ROM, decoded from the module of **bank 0x0C** (bank 12, file <c>0x18000</c>).
///
/// In the ROM the ending (<c>end_script</c> 0x7353, banks 1-3) calls <c>print_12</c> (0x771C), which pages in
/// bank 0x0C and prints the stream. The addresses used, and what each one contains:
///
/// <list type="table">
/// <item><description><c>0xACC3</c> — <c>end_boot</c> ("congratulations…")</description></item>
/// <item><description><c>0xAD12</c> — <c>end_txt2</c> ("all you have to do now…", with the original's "swich" typo)</description></item>
/// <item><description><c>0xAD58</c> — <c>end_txt3</c> ("push space key")</description></item>
/// <item><description><c>0xAEDD</c> — <c>end_txt4</c> ("thanks to your courage…")</description></item>
/// <item><description><c>0xAF21</c> — <c>end_txt5</c> ("congratulations")</description></item>
/// </list>
///
/// **Stream format** (measured): <c>[D][E]</c> = initial position (x, y in MSX screen pixels); then
/// <c>0xFE x y</c> = **repositions**; <c>0x00</c> = space; <c>0xFF</c> = end; and the glyphs are
/// **ASCII + 0x7C** (e.g.: <c>0xE1</c> = <c>'e'</c>, <c>0xDF</c> = <c>'c'</c>, <c>0xDD</c> = <c>'a'</c>).
/// Three bytes have no ASCII match and were left out (notes below): <c>0xDB</c>, <c>0xF7</c>
/// and the pair <c>0xFA 0xFB</c> — they are special glyphs of the module's tileset, not letters.
/// </summary>
public static class EndingTexts
{
    /// <summary>One screen line: position (x, y) in MSX screen pixels and the text.</summary>
    public readonly record struct Line(int X, int Y, string Text);

    /// <summary><c>end_boot</c> (0xACC3): (72,72) + repositions (0x40,0x58), (0x48,0x68), (0x40,0x78), (0x40,0x88).</summary>
    public static readonly Line[] Boot =
    [
        new(72, 72, "congratulations"),
        new(64, 88, "to vick x"),
        new(72, 104, "you heroically"),
        new(64, 120, "unsealed all the"),
        new(64, 136, "locks"),
    ];

    /// <summary><c>end_txt2</c> (0xAD12): (40,128) + repositions (0x28,0x90), (0x28,0xA0).</summary>
    public static readonly Line[] Text2 =
    [
        new(40, 128, "all you have to do now"),
        new(40, 144, "is turn off the swich"),
        new(40, 160, "to save the world"),
    ];

    /// <summary><c>end_txt3</c> (0xAD58): (72,160) — the screen that waits for the key.</summary>
    public static readonly Line[] Text3 =
    [
        new(72, 160, "push space key"),
    ];

    /// <summary><c>end_txt4</c> (0xAEDD): (48,48) + repositions (0x10,0x50), (0x58,0x70).</summary>
    public static readonly Line[] Text4 =
    [
        new(48, 48, "thanks to your courage"),
        new(16, 80, "the world has been saved"),
        new(88, 112, "well done"),
    ];

    /// <summary><c>end_txt5</c> (0xAF21): (56,16).</summary>
    public static readonly Line[] Text5 =
    [
        new(56, 16, "congratulations"),
    ];
}
