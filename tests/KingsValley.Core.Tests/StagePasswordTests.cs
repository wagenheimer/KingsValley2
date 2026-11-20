using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

public sealed class StagePasswordTests
{
    /// <summary>
    /// Proof from the original game: the longplay shows <c>EAEAODGD</c> at the end of stage 1 (the same
    /// frame in which the screen shows "REST 01"). The 4th byte (<c>0x63</c>) is the sum of the first three
    /// (<c>0x40 + 0x40 + 0xE3</c>) and rotating by <c>0xE3 &amp; 7 = 3</c> gives back <c>(0x02, 0x02)</c>.
    /// </summary>
    [Fact]
    public void Decode_LongplayClearScreenCode_GivesStage2WithTwoRest()
    {
        Assert.True(StagePassword.TryDecode("EAEAODGD", out int stage, out int rest));
        Assert.Equal(2, stage);
        Assert.Equal(2, rest);
    }

    /// <summary>The original rotates the pair (0xE242, 0xE240) by the drawn <c>R &amp; 7</c> — any rotation holds.</summary>
    [Fact]
    public void Decode_AcceptsAnyRotation()
    {
        // Same (stage, REST) = (2, 2) written with R = 3 (three-bit rotation); R = 0 is the canonical form.
        Assert.True(StagePassword.TryDecode("EAEAADID", out int s3, out int r3));
        Assert.Equal((2, 2), (s3, r3));

        Assert.True(StagePassword.TryDecode(StagePassword.Encode(2, 2), out int s0, out int r0));
        Assert.Equal((2, 2), (s0, r0));
    }

    [Theory]
    [InlineData(1, 1, "ABABAAAC")]
    [InlineData(2, 2, "ACACAAAE")]
    [InlineData(18, 99, "BCGDAAHF")]
    [InlineData(60, 99, "DMGDAAJP")]
    public void Encode_WritesCanonicalCode(int stage, int rest, string expected) =>
        Assert.Equal(expected, StagePassword.Encode(stage, rest));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(16, 7)]
    [InlineData(60, 99)]
    public void Decode_RoundTripsEncode(int stage, int rest)
    {
        Assert.True(StagePassword.TryDecode(StagePassword.Encode(stage, rest), out int s, out int r));
        Assert.Equal(stage, s);
        Assert.Equal(rest, r);
    }

    /// <summary>The special ones are compared <b>before</b> this verification (original: <c>6A83</c>).</summary>
    [Theory]
    [InlineData("")]
    [InlineData("FESTIVAL")]
    [InlineData("TRYAGAIN")]
    [InlineData("ABABAAAB")] // sum of the first three (0x01) != the fourth (0x01 + 1)
    [InlineData("DNABAADO")] // stage 61 (o original recusa >= 0x3D em 6AE1)
    [InlineData("ABACAAAB")] // sum of the first three (0x03) != the fourth (0x01)
    [InlineData("ABAAAAAB")] // REST 0 (o original recusa em 6AF1)
    public void Decode_RejectsInvalid(string password) =>
        Assert.False(StagePassword.TryDecode(password, out _, out _));

    [Fact]
    public void Encode_RejectsOutOfRangeStage() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StagePassword.Encode(0, 1));

    /// <summary>
    /// The stage-end screen writes the password <b>after</b> the <c>INC (0xE240)</c> of <c>732C</c>:
    /// the longplay shows "REST 01" and the password that gives back REST 2.
    /// </summary>
    [Fact]
    public void ClearScreenPassword_IsOneRestAheadOfTheShownValue()
    {
        var run = GameRun.New(0, stage: 1, rest: 0).StageCleared();

        Assert.Equal(1, run.Rest);
        Assert.Equal(StagePassword.Encode(run.Stage, run.Rest + 1), run.ClearScreenPassword);

        Assert.True(StagePassword.TryDecode(run.ClearScreenPassword, out int stage, out int rest));
        Assert.Equal((2, 2), (stage, rest));
    }
}
