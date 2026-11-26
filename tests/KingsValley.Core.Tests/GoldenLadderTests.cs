using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Core.Tests;

public sealed class GoldenLadderTests
{
    [LocalRomFact("msx1")]
    public void OriginalMsx1InitializesTwoUses()=>AssertNativeInitialCounter("kv2_msx1.rom");
    [LocalRomFact("msx2")]
    public void OriginalMsx2InitializesTwoUses()=>AssertNativeInitialCounter("kv2_msx2_scc.rom");
    private static void AssertNativeInitialCounter(string name)
    {
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms",name);
        byte[] rom=File.ReadAllBytes(path);var machine=new MiniMsx(rom,RomMapper.KonamiScc);machine.Boot(2_000_000);
        RomStageState.Read(machine,RomStageState.ResolveStageLoader(rom),9);
        Assert.NotEqual(0,machine.Memory(0xE7C0));Assert.Equal(2,machine.Memory(0xE7C3)>>6);
    }
    private static StageSession Session()
    {
        var json=StageTestData.Json(new[]{StageTestData.Box()}).Replace("\"goldenLadders\":[]",
            "\"goldenLadders\":[{\"room\":1,\"col\":8,\"row\":10,\"length\":8}]");
        var session=new StageSession(StageTestData.Stage(json));
        var state=session.CaptureState();state.Player.X=64;state.Player.Y=112;state.Player.State=PlayerState.Climbing;
        session.RestoreFrom(state);return session;
    }
    private static readonly PlayerInput Up=new(false,false,true,false,false);
    private static readonly PlayerInput Down=new(false,false,false,true,false);

    [Fact]
    public void IdleClimbingDoesNotExpireButTwoClimbExitsRemoveLadderAndChangeCollision()
    {
        var session=Session();var ladder=session.Definition.GoldenLadders[0];
        for(int i=0;i<100;i++)session.Tick(PlayerInput.None);
        Assert.Equal(2,session.GoldenLadderUsesRemaining(ladder));
        for(int i=0;i<40 && session.GoldenLadderUsesRemaining(ladder)==2;i++)session.Tick(Up);
        Assert.Equal(1,session.GoldenLadderUsesRemaining(ladder));Assert.Single(session.ActiveGoldenLadders);
        // A save in the middle must retain the consumed use and the armed state.
        var restored=new StageSession(session.Definition);restored.RestoreFrom(session.CaptureState());
        restored.Tick(Down);restored.Tick(Up);
        Assert.Equal(0,restored.GoldenLadderUsesRemaining(ladder));Assert.Empty(restored.ActiveGoldenLadders);
        Assert.Equal(TerrainKind.Soft,restored.Map.GetTerrain(8,10));
        Assert.Equal(TerrainKind.Soft,restored.Map.GetTerrain(9,10));
        Assert.Equal(TerrainKind.Empty,restored.Map.GetTerrain(8,11));
        Assert.Equal(TerrainKind.Empty,restored.Map.GetTerrain(9,17));
        Assert.Contains(restored.Events,e=>e.Sound==OriginalSounds.GoldenLadder);
        var removed=new StageSession(session.Definition);removed.RestoreFrom(restored.CaptureState());
        Assert.Empty(removed.ActiveGoldenLadders);Assert.Equal(TerrainKind.Empty,removed.Map.GetTerrain(8,11));
    }
}
