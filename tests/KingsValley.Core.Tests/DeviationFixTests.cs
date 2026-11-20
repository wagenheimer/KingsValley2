using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;

namespace KingsValley.Core.Tests;

/// <summary>
/// Rules that closed deviations against the ROM: the password cheats (<c>pwd_cheat</c> 0x6B24 →
/// <c>play_clear</c>, which writes <c>01 01 01 00 00 03</c> into <c>E240</c>) and the pushable stone
/// collision (the map <c>get</c> returns wall on the cells marked by <c>SetRock</c>).
/// </summary>
public sealed class DeviationFixTests
{
    [Fact]
    public void NewGame_DoesNotEnableContinue()
    {
        Assert.False(GameRun.New(hiScore: 0).ContinueEnabled);
    }

    [Fact]
    public void TryAgain_RestartsAtStage1WithOneRestAndEnablesContinue()
    {
        var run = GameRun.Cheat(hiScore: 0, continueEnabled: true);

        Assert.Equal(1, run.Stage);
        Assert.Equal(1, run.Rest);
        Assert.Equal(0, run.Score);
        Assert.True(run.ContinueEnabled);
        Assert.False(run.Invincible);
    }

    [Fact]
    public void Festival_RestartsAtStage1WithOneRestAndIsInvincible()
    {
        var run = GameRun.Cheat(hiScore: 0, invincible: true);

        Assert.Equal(1, run.Stage);
        Assert.Equal(1, run.Rest);
        Assert.True(run.Invincible);
        Assert.False(run.ContinueEnabled);
    }

    [Fact]
    public void RockCells_AreSolidWhileTheStoneSitsThere()
    {
        var stage = StageTestData.Stage(StageTestData.Json([StageTestData.Box()]));
        var map = new RoomMap(stage.Rooms[0], stage.Ladders);

        // (5,10) is empty in the test scenario.
        Assert.False(map.IsSolid(5 * 8 + 3, 10 * 8 + 3));

        map.SetRock(5, 10, true);
        Assert.Equal(TerrainKind.Wall, map.Get(5, 10));
        Assert.True(map.IsSolid(5 * 8 + 3, 10 * 8 + 3));

        map.SetRock(5, 10, false);
        Assert.False(map.IsSolid(5 * 8 + 3, 10 * 8 + 3));
    }
}
