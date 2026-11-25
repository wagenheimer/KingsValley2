using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Core.Tests;

public class FallingWallTests
{
    [Fact]
    public void PassingUnderTheColumnBuildsOneDiggableCellEveryThirtyTicksAndSurvivesSaving()
    {
        var rows=StageTestData.Box();rows[6]=new string('#',32);
        var definition=StageTestData.Stage(StageTestData.Json(new[]{rows},
            mechanisms:"[{\"type\":3,\"room\":1,\"x\":160,\"y\":16,\"raw\":\"030010A0010000000400\"}]"));
        var session=new StageSession(definition);session.PlacePlayer(1,152,32);
        session.Tick(PlayerInput.None);session.PlacePlayer(1,100,32);
        for(int i=0;i<29;i++)session.Tick(PlayerInput.None);
        Assert.Equal(TerrainKind.Empty,session.Map.Get(20,2));
        session.Tick(PlayerInput.None);
        Assert.Equal(TerrainKind.Soft,session.Map.Get(20,2));
        Assert.Contains(session.Events,e=>e.Sound==0x31);
        for(int i=0;i<15;i++)session.Tick(PlayerInput.None);
        var restored=new StageSession(definition);restored.RestoreFrom(session.CaptureState());
        for(int i=0;i<110;i++){session.Tick(PlayerInput.None);restored.Tick(PlayerInput.None);}
        Assert.Equal(session.Map.Snapshot(),restored.Map.Snapshot());
        for(int row=2;row<6;row++)Assert.Equal(TerrainKind.Soft,restored.Map.Get(20,row));
        for(int row=2;row<6;row++)Assert.True(restored.Map.Dig(20,row));
        restored.PlacePlayer(1,152,32);
        for(int i=0;i<160;i++)restored.Tick(PlayerInput.None);
        Assert.Equal(TerrainKind.Empty,restored.Map.Get(20,2));
    }

    [LocalRomFact("msx1")]
    public void LevelThreeWallMatchesTheNativeMsx1MapAtEveryTick()=>Native("msx1",0xBA3B,0xBA5D,0xB9EF);

    [LocalRomFact("msx2")]
    public void LevelThreeWallMatchesTheNativeMsx2MapAtEveryTick()=>Native("msx2_scc",0xBCC0,0xBCE2,0xBC74);

    private static void Native(string variant,ushort tick,ushort draw,ushort trigger)
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley","roms","kv2_"+variant+".rom"));
        var session=new StageSession(StageLoader.Parse(RomStageJson.Build(rom,3)));
        session.Coffins.Clear();session.Enemies.Clear();session.PlacePlayer(2,152,32);
        var machine=new MiniMsx(rom,RomMapper.KonamiScc);Assert.True(machine.Boot(2_000_000));
        RomStageState.Read(machine,RomStageState.ResolveStageLoader(rom),3);
        machine.SetBank(0x6000,1);machine.SetBank(0x8000,2);machine.SetBank(0xA000,3);
        ushort slot=Enumerable.Range(0,16).Select(i=>(ushort)(0xE600+i*16)).Single(a=>machine.Memory(a)==3);
        void Call(ushort routine)
        {
            byte[] stub={0xDD,0x21,(byte)slot,(byte)(slot>>8),0xCD,(byte)routine,(byte)(routine>>8),0xC9};
            for(int i=0;i<stub.Length;i++)machine.Write((ushort)(0xF100+i),stub[i]);
            machine.CallAddress(0xF100,200_000);
        }
        machine.Write(0xE243,2);machine.Write(0xE284,152);machine.Write(0xE282,32);
        // Trigger in room 2, then leave. Native offscreen stamping avoids graphics/BIOS dependencies.
        Call(tick);Call(trigger);session.Tick(PlayerInput.None);
        session.PlacePlayer(1,104,160);machine.Write(0xE243,1);machine.Write(0xE284,104);machine.Write(0xE282,160);
        for(int frame=0;frame<155;frame++)
        {
            int previous=machine.Memory((ushort)(slot+5));
            if(machine.Memory(slot)==3)
            {
                Call(tick);
                if(machine.Memory(slot)==3 && machine.Memory((ushort)(slot+5))!=previous)Call(draw);
            }
            Call(trigger);session.Tick(PlayerInput.None);
            for(int row=2;row<6;row++)
            {
                int packed=machine.Memory((ushort)(0xE840+2*0xC0+row*8+20/4));
                Assert.Equal((packed>>6)&3,(int)session.Room(2).Get(20,row));
            }
            Assert.DoesNotContain(session.Events,e=>e.Sound==0x31);
        }
        for(int row=2;row<6;row++)Assert.Equal(TerrainKind.Soft,session.Room(2).Get(20,row));
    }
}
