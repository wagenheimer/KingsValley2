using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

public sealed class GameRunTests
{
    [Fact]
    public void StageCleared_AdvancesStage_AndGivesOneRest()
    {
        var run = GameRun.New(hiScore: 0).StageCleared();

        Assert.Equal(2, run.Stage);
        Assert.Equal(1, run.Rest);
    }

    [Fact]
    public void DyingWithNoRest_IsGameOver()
    {
        var run = GameRun.New(hiScore: 0).Died();

        Assert.True(run.IsGameOver);
    }

    [Fact]
    public void AddScore_UpdatesHiScore()
    {
        var run = GameRun.New(hiScore: 1000).AddScore(1500);

        Assert.Equal(1500, run.HiScore);
    }

    [Fact]
    public void GainLife_AddsRest_AndCapsAtNinetyNine()
    {
        // 4C20 add_score: E240 += 1 in BCD, cap 0x99
        Assert.Equal(1, GameRun.New(hiScore: 0).GainLife().Rest);
        Assert.Equal(99, GameRun.New(hiScore: 0, rest: 99).GainLife().Rest);
    }

    [Fact]
    public void Password_EncodesStageAndRest()
    {
        var run = GameRun.New(hiScore: 0, stage: 18, rest: 99);

        Assert.Equal("BCGDAAHF", run.Password);
        Assert.True(Stages.StagePassword.TryDecode(run.Password, out int stage, out int rest));
        Assert.Equal((18, 99), (stage, rest));
    }

    [Fact]
    public void RealStages_LoadWhenExtracted()
    {
        // The ROM data stays out of git; the test only runs when it has been generated.
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/KingsValley.Desktop/Content/Rom/Levels"));
        if (!File.Exists(Path.Combine(dir, "stage01.json")))
            return;

        for (int n = 1; n <= 60; n++)
        {
            var stage = StageLoader.Load(dir, n);
            var session = new StageSession(stage);
            Assert.Equal(n, stage.Number);
            Assert.NotEmpty(stage.SoulStones);
            Assert.Equal(stage.Rooms.Count, stage.RoomGrid.Cast<int>().Count(c => c != 0));
            session.Tick(PlayerInput.None);
        }
    }
}
