namespace KingsValley.Core.Stages;

/// <summary>
/// King's Valley II passwords (MSX2): 8 letters from <c>A</c> to <c>P</c>.
///
/// <para><b>How the original generates it</b> — <c>6994</c>, called by <c>697C</c> on the stage-end screen:
/// the work bytes are <c>EDE2 = (0xE242)</c> (stage), <c>EDE3 = (0xE240)</c> (REST),
/// <c>EDE4 = R</c> (the Z80 refresh register — random) and <c>EDE5 = sum of the three</c>.
/// The pair (stage, REST) is rotated 16 bits to the <b>right</b> by <c>R &amp; 7</c> and each byte becomes two
/// letters through <c>69E0</c>/<c>69F5</c> (<c>'A' + nibble</c>, high nibble first).</para>
///
/// <para><b>How the original verifies it</b> — <c>6A98</c> (the same password screen): converts the letters to
/// values (letter − <c>0xE1</c>, digit − <c>0xD0</c>), requires all &lt; <c>0x10</c>, checks that the
/// sum of the first three bytes is the fourth (<c>6AA8</c>) and rotates the pair to the <b>left</b> by
/// <c>b2 &amp; 7</c> to recover (stage, REST); rejects stage 0 / ≥ <c>0x3D</c> and REST 0.</para>
///
/// <para><b>The port writes the canonical form</b> (<c>R = 0</c>, no rotation): the code is
/// deterministic and stays valid — the verification accepts any rotation, and the <i>regeneration</i>
/// of the screen on every cleared stage is random precisely because of <c>R</c>.</para>
///
/// <para><b>Factual proof</b> (what the original shows): the longplay on stage 2 displays
/// <c>EAEAODGD</c>, whose 4th byte <c>0x63</c> is exactly <c>0x40 + 0x40 + 0xE3</c> (the sum) and which,
/// rotated back by <c>0xE3 &amp; 7 = 3</c>, gives <c>(0x02, 0x02)</c> — stage 2 with REST 2. See
/// <see cref="StagePassword.TryDecode"/> accepting that code in the test.</para>
///
/// <para>Special passwords, compared <b>before</b> the arithmetic (ASCII tables at <c>6B57</c> and
/// <c>6B5F</c>): <c>FESTIVAL</c> (invincible) and <c>TRYAGAIN</c> (continue after the game over).</para>
///
/// <para>⚠️ Until the out/2026 revision the port used a third-party calculator format
/// (<c>[lives%10][stage/16][stage%16][lives/10]</c> + <c>AMCN</c>): it does <b>not</b> satisfy the
/// original's checksum and the <c>AMCN</c> chain does not exist in the ROM (binary search across all
/// 16 pages). It was replaced.</para>
/// </summary>
public static class StagePassword
{
    /// <summary>Invincibility password (<c>6B57</c>).</summary>
    public const string Festival = "FESTIVAL";

    /// <summary>"Continue" password (<c>6B5F</c>).</summary>
    public const string TryAgain = "TRYAGAIN";

    /// <summary>Smallest representable stage (the original rejects 0 at <c>6AEB</c>).</summary>
    public const int MinStage = 1;

    /// <summary>Largest representable stage: <c>6AE1</c> compares with <c>0x3D</c> (61).</summary>
    public const int MaxStage = Common.GameConstants.TotalStages;

    /// <summary>
    /// Smallest representable REST. The original does <b>not</b> represent REST 0: <c>6AF1</c> treats the zeroed
    /// byte as a wrong password — and in practice the stage-end screen already adds the "special bonus".
    /// </summary>
    public const int MinRest = 1;

    /// <summary>Largest representable REST (the original counter saturates at <c>0x99</c> BCD).</summary>
    public const int MaxRest = Common.GameConstants.MaxRest;

    /// <summary>
    /// Generates the canonical password for a stage with N extra lives. Uses <c>R = 0</c> (no rotation): the
    /// result is deterministic and equally accepted by the original.
    /// </summary>
    public static string Encode(int stage, int rest)
    {
        if (stage is < MinStage or > MaxStage)
            throw new ArgumentOutOfRangeException(nameof(stage), stage, $"Stage must be between {MinStage} and {MaxStage}.");
        if (rest is < 0 or > MaxRest)
            throw new ArgumentOutOfRangeException(nameof(rest), rest, $"REST must be between 0 and {MaxRest}.");

        // 6994: EDE2 = stage, EDE3 = REST, EDE4 = R (0 = canonical), EDE5 = sum of the three.
        int b0 = stage, b1 = rest, b2 = 0, b3 = (b0 + b1 + b2) & 0xFF;

        var chars = new char[8];
        WriteByte(chars, 0, b0);   // 69E0: high nibble, then the low one
        WriteByte(chars, 2, b1);
        WriteByte(chars, 4, b2);
        WriteByte(chars, 6, b3);
        return new string(chars);
    }

    /// <summary>
    /// Reads a stage password (the rules of <c>6A98</c>). Returns false for an invalid password —
    /// including the special ones, which the caller compares first (like the original at <c>6A83</c>).
    /// </summary>
    public static bool TryDecode(string password, out int stage, out int rest)
    {
        stage = 0;
        rest = 0;

        if (string.IsNullOrEmpty(password) || password.Length != 8)
            return false;

        Span<int> n = stackalloc int[8];
        int sum = 0;

        for (int i = 0; i < password.Length; i++)
        {
            // 6A71: letter - 0xE1 (A..Z -> 0..25), digit - 0xD0 (0..9).
            char c = char.ToUpperInvariant(password[i]);
            int value = c switch
            {
                >= 'A' and <= 'Z' => c - 'A',
                >= '0' and <= '9' => c - '0',
                _ => -1,
            };
            if (value is < 0 or > 15)   // 6A9D: value >= 0x10 -> "WRONG PASSWORD"
                return false;

            n[i] = value;
            sum += value;
        }

        if (sum == 0)                   // 6A94
            return false;

        int b0 = (n[0] << 4) | n[1];
        int b1 = (n[2] << 4) | n[3];
        int b2 = (n[4] << 4) | n[5];
        int b3 = (n[6] << 4) | n[7];

        if (((b0 + b1 + b2) & 0xFF) != b3)  // 6AA8: sum of the first three = the fourth
            return false;

        // 6AD5: rotates the pair (b0, b1) to the left by b2 & 7 (the inverse of the right rotation of 6994).
        int k = b2 & 7;
        int pair = ((b0 << 8) | b1) & 0xFFFF;
        pair = ((pair << k) | (pair >> (16 - k))) & 0xFFFF;

        int decodedStage = pair >> 8;
        int decodedRest = pair & 0xFF;

        if (decodedStage is < MinStage or > MaxStage)   // 6AE1: CP 0x3D
            return false;
        if (decodedRest is < MinRest or > MaxRest)      // 6AF1: zero is a wrong password
            return false;

        stage = decodedStage;
        rest = decodedRest;
        return true;
    }

    /// <summary>69E0: two characters per byte, <c>'A'</c> + nibble, most significant first.</summary>
    private static void WriteByte(char[] target, int index, int value)
    {
        target[index] = (char)('A' + ((value >> 4) & 0x0F));
        target[index + 1] = (char)('A' + (value & 0x0F));
    }
}
