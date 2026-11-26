using System.Security.Cryptography;
using System.Text.Json;
using KingsValley.Core.Common;
using KingsValley.Core.Gameplay;
using KingsValley.Core.SaveSystem;
using KingsValley.Core.Stages;
using KingsValley.Core.Rooms;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Core.Tests;

public sealed class ContestRomFactAttribute : FactAttribute
{
    public ContestRomFactAttribute()
    {
        string? path=Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_CONTEST_ROM");
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))Skip="Set KINGSVALLEY_TEST_CONTEST_ROM to the locally owned verified Contest ROM or ZIP.";
    }
}
public sealed class ContestCampaignTests
{
    [Fact]
    public void ContestCompletionReturnsAfter360TicksWithoutPasswordOrKeyWait()
    {
        var definition=StageTestData.Stage(StageTestData.Json(new[]{StageTestData.Box()}));
        var session=new StageSession(definition,Campaigns.Contest);
        session.DebugEnterDoor();
        for(int tick=0;tick<300 && session.Exit!=ExitPhase.ClearScreen;tick++)session.Tick(PlayerInput.None);
        Assert.Equal(ExitPhase.ClearScreen,session.Exit);
        for(int tick=0;tick<StageSession.ContestClearTicks-1;tick++)session.Tick(PlayerInput.None);
        Assert.Equal(StageStatus.Playing,session.Status);Assert.Equal(ExitClearStep.Texts,session.ClearStep);
        var restored=new StageSession(definition,Campaigns.Contest);restored.RestoreFrom(session.CaptureState());
        session.Tick(PlayerInput.None);restored.Tick(PlayerInput.None);
        Assert.Equal(StageStatus.Cleared,session.Status);Assert.Equal(ExitClearStep.None,session.ClearStep);
        Assert.Equal(StageStatus.Cleared,restored.Status);
    }
    [Fact]
    public void ContestRunAndLegacyOriginalHaveIndependentStageBounds()
    {
        Assert.False(GameRun.New(0,20).IsLastStage);
        Assert.True(GameRun.New(0,60).IsLastStage);
        var contest=GameRun.New(0,99,campaignId:Campaigns.Contest);
        Assert.Equal(20,contest.Stage);Assert.True(contest.IsLastStage);
        Assert.Equal(20,contest.StageCleared().Stage);Assert.Equal(Campaigns.Contest,contest.Died().CampaignId);
        Assert.Equal(60,Campaigns.StageCount(Campaigns.Original));
    }
    [Fact]
    public void LegacyProgressSurvivesAndContestProgressAndSlotsAreIndependent()
    {
        var save=JsonSerializer.Deserialize<SaveGameData>("{\"HighestLevel\":12,\"HiScore\":1234,\"CompletedLevels\":[1,2]}")!;
        save.ProgressFor(Campaigns.Contest).CompletedLevels.Add(7);
        save.ProgressFor(Campaigns.Contest).HiScore=777;
        var restored=JsonSerializer.Deserialize<SaveGameData>(JsonSerializer.Serialize(save))!;
        Assert.Equal(1234,restored.HiScore);Assert.Equal(12,restored.HighestLevel);
        Assert.Equal(new[]{1,2},restored.CompletedLevels);Assert.Equal(new[]{7},restored.ContestProgress.CompletedLevels);
        Assert.EndsWith(Path.Combine("contest","slot1.json"),SaveStateService.GetJsonPath(1,Campaigns.Contest));
        Assert.NotEqual(SaveStateService.GetJsonPath(1),SaveStateService.GetJsonPath(1,Campaigns.Contest));
        Assert.Equal(Campaigns.Original,new GameRunData().CampaignId);
        var themed=new GameRunData{CampaignId=Campaigns.Contest,ContestPyramid=6};
        Assert.Equal(6,JsonSerializer.Deserialize<GameRunData>(JsonSerializer.Serialize(themed))!.ContestPyramid);
        Assert.Equal(1,new GameRunData().ContestPyramid);
    }
    [Fact]
    public void ContestCannotInheritOriginalGraphicsDespiteSharingMsx2Hardware()
    {
        string root=Path.Combine(Path.GetTempPath(),"contest-chain-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root,"original"));File.WriteAllText(Path.Combine(root,"original","only.dat"),"original");
            var original=new ArtSet("msx2","MSX2",Path.Combine(root,"original"),ArtFlavor.Msx2);
            var contest=new ArtSet("contest","Contest",Path.Combine(root,"contest"),ArtFlavor.Msx2,"msx2",CartridgeVariant:CartridgeVariants.Contest);
            Assert.Equal(Path.Combine(root,"contest","only.dat"),ArtSets.Resolve(new[]{original,contest},contest,"only.dat"));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [ContestRomFact]
    public void NativeGoldenLadderArmsConsumesTwiceAndStampsTheReplacementTerrain()
    {
        byte[] rom=File.ReadAllBytes(RomFile.Resolve(Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_CONTEST_ROM")!));
        var native=new Msx2VdpRunner(rom);native.BootContest();ContestCartridge.Prepare(native,1);
        native.SetMemory(0xE243,1);native.SetMemory(0xE282,64);native.SetMemory(0xE284,8);
        Assert.Equal(0x88,native.Memory(0xE7C3));
        void Arm(){native.SetMemory(0xE280,2);native.SetMemory(0xE2A7,0);native.Invoke(0x8FD5);}
        void Pulse(){native.SetMemory(0xE280,0);native.SetMemory(0xE2A7,1);native.Invoke(0x8FD5);}
        Arm();Assert.Equal(0xA8,native.Memory(0xE7C3));
        Pulse();Assert.Equal(0x48,native.Memory(0xE7C3));
        Pulse();Assert.Equal(0x48,native.Memory(0xE7C3)); // A strobe without rearming is not another use.
        Arm();Pulse();Assert.Equal(0,native.Memory(0xE7C0));
        int Cell(int col,int row)=>(native.Memory((ushort)(0xE900+row*8+col/4))>>(6-2*(col%4)))&3;
        Assert.Equal(2,Cell(1,8));Assert.Equal(2,Cell(2,8));
        Assert.Equal(0,Cell(1,9));Assert.Equal(0,Cell(2,15));
    }
    [ContestRomFact]
    public void ContestZipProducesTwentyDistinctNativeStagesAndValidIndependentGraphics()
    {
        string source=Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_CONTEST_ROM")!;
        string root=Path.Combine(Path.GetTempPath(),"contest-audit-"+Guid.NewGuid().ToString("N"));
        try
        {
            var info=RomFolderManager.InspectRomFile(source);Assert.NotNull(info);
            Assert.True(info.IsSupported);Assert.Equal(CartridgeVariants.Contest,info.Variant);
            byte[] rom=File.ReadAllBytes(RomFile.Resolve(source));
            var context=GraphicsExtractionContext.Create(rom,CartridgeVariants.Contest,root);
            Assert.Equal(20,context.StageLimit);Assert.Equal(ArtFlavor.Msx2,context.Flavor);
            var result=GraphicsExtractionCoordinator.Extract(context);
            Assert.True(result.Manifest.Ready,string.Join("; ",result.Manifest.Checks.Where(c=>c.State!="valid")));
            Assert.True(result.Manifest.Verify(result.Directory,CartridgeVariants.Contest,ContestCartridge.Sha1));
            Assert.False(result.Manifest.Verify(result.Directory,"msx2"));
            Assert.Equal(20,Directory.GetFiles(Path.Combine(result.Directory,"Levels"),"stage*.json").Length);
            var levels=Enumerable.Range(1,20).Select(n=>StageLoader.Load(Path.Combine(result.Directory,"Levels"),n)).ToArray();
            Assert.All(levels,d=>{Assert.Equal(1,d.Pyramid);Assert.Equal(0,d.MusicIndex);});
            // SCREEN SELECT changes only the presentation; extracted terrain and identity stay intact.
            var themed=levels[0].WithPyramid(6);
            Assert.Equal(6,themed.Pyramid);Assert.Equal(1,levels[0].Pyramid);
            Assert.Same(levels[0].Rooms,themed.Rooms);Assert.Equal(levels[0].MusicIndex,themed.MusicIndex);
            Assert.Throws<ArgumentOutOfRangeException>(()=>levels[0].WithPyramid(7));
            string ScreenHash(string name)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(result.Directory,"Screens",name+".bin"))));
            Assert.Equal(4,Enumerable.Range(0,4).Select(g=>ScreenHash($"contest_publication_{g}")).Distinct().Count());
            Assert.Equal(20,Enumerable.Range(0,20).Select(n=>ScreenHash($"contest_author_{n/5}_{n%5}")).Distinct().Count());
            Assert.Equal(6,Enumerable.Range(1,6).Select(n=>ScreenHash($"contest_theme_{n}")).Distinct().Count());
            Assert.Equal(3,levels[0].PlayerStart.Room);Assert.Equal(120,levels[0].PlayerStart.X);Assert.Equal(136,levels[0].PlayerStart.Y);
            Assert.Equal(20,levels.Select(d=>string.Concat(d.Rooms.SelectMany(r=>r.Cells).Select(x=>(byte)x))).Distinct().Count());
            Assert.False(Directory.Exists(Path.Combine(root,"kings-valley-2","msx2")));
            // Stage 3 has no mechanisms. Remove dynamic stones and compare the clean room
            // against the cartridge's own SCREEN 5 writer, including its golden ladders.
            var native=new Msx2VdpRunner(rom);native.BootContest();ContestCartridge.Prepare(native,3);
            for(int i=0;i<128;i++)native.SetMemory((ushort)(0xE700+i),0);
            native.SetMemory(0xE243,1);native.Invoke(0x434F);
            var def=levels[2];
            byte[] actual=RoomAssembler.Assemble(1,1,1,File.ReadAllBytes(Path.Combine(result.Directory,"Rooms","blocks_p1.bin")),
                RomDirectExtractor.MuralCodes(new(rom),1,"even"),Array.Empty<DecorationEntry>(),def.Rooms[0].Cells,def.Ladders,def.GoldenLadders);
            Assert.Equal(native.SnapshotScreen5().Indices.Take(256*192).ToArray(),actual);
            // Independent native 47CF trace: curtain → congratulations → mode 03/03 selection.
            var completion=new Msx2VdpRunner(rom);completion.BootContest();ContestCartridge.Prepare(completion,1);
            completion.SetMemory(0xE243,3);completion.Invoke(0x434F);completion.Invoke(0x5679);completion.Invoke(0x4D99,hl:0x5C1A);
            completion.SetMemory(0xE200,9);completion.SetMemory(0xE201,0);
            completion.SetMemory(0xE257,3);completion.SetMemory(0xE258,0);completion.SetMemory(0xE259,4);
            for(int tick=0;tick<48;tick++)completion.Invoke(0x44BC);
            Assert.Equal(1,completion.Memory(0xE201));Assert.Equal(180,completion.Memory(0xE204));
            var clear=KingsValley.Core.Screens.ScreenAssembler.DecodeFile(Path.Combine(result.Directory,"Screens","contest_clear.bin"));
            Assert.Equal(completion.SnapshotScreen5().Indices,clear.Indices);
            for(int tick=0;tick<359;tick++)completion.Invoke(0x44BC);
            Assert.Equal(9,completion.Memory(0xE200));
            completion.Invoke(0x44BC);Assert.Equal(3,completion.Memory(0xE200));Assert.Equal(3,completion.Memory(0xE201));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
