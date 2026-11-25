using KingsValley.Core.Gameplay;

namespace KingsValley.Core.Tests;

/// <summary>
/// Pyramid FX table (<c>0xB548</c>) read by <c>load_ef10</c> (0xB400): the value <b>persists</b>
/// when the stage has no record, so on stage 60 it is 2 — <c>end_menu</c> opens the puzzle.
/// </summary>
public sealed class StageFxTests
{
    [Fact]
    public void BeforeTheFirstRecord_ThereIsNoFx()
    {
        Assert.Equal(0, StageFx.Ef10At(1));
        Assert.Equal(0, StageFx.Ef10At(6));
    }

    [Fact]
    public void Records_MapStagesToTheRightPage()
    {
        Assert.Equal(1, StageFx.Ef10At(7));
        Assert.Equal(2, StageFx.Ef10At(20));
        Assert.Equal(1, StageFx.Ef10At(40));
        Assert.Equal(2, StageFx.Ef10At(55));
    }

    [Fact]
    public void ValuePersistsUntilTheNextRecord()
    {
        // 8..19 keep the record from stage 7 (EF10 = 1).
        Assert.Equal(1, StageFx.Ef10At(8));
        Assert.Equal(1, StageFx.Ef10At(19));
        // 21..23 keep the one from 20 (EF10 = 2).
        Assert.Equal(2, StageFx.Ef10At(21));
    }

    [Fact]
    public void Stage60_EndsInThePuzzle()
    {
        Assert.Equal(2, StageFx.Ef10At(60));
        Assert.False(StageFx.OpensSoundSelect(60));
        Assert.True(StageFx.OpensSoundSelect(7));
    }

    [Fact]
    public void RecordOf_ReturnsTheVisualFields()
    {
        var record = StageFx.RecordOf(24);

        Assert.NotNull(record);
        Assert.Equal(1, record!.Value.Ef10);
        Assert.Equal(4, record.Value.Kind);
        Assert.Equal(40, record.Value.X);
        Assert.Equal(224, record.Value.Y);
    }
}
