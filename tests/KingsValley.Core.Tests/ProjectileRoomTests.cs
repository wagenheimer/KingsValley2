using KingsValley.Core.Gameplay;
using KingsValley.Core.Stages;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Core.Tests;

public class ProjectileRoomTests
{
    [Theory]
    [InlineData(Facing.Right)]
    [InlineData(Facing.Left)]
    public void KnifeCrossesIntoTheOtherRoomAndLandsThereWithoutReturning(Facing facing)
    {
        var source=StageTestData.Box(); var destination=StageTestData.Box();
        for(int row=20;row<=21;row++)
        {
            source[row]=new string('.',32);
            destination[row]=facing==Facing.Right?new string('.',30)+"##":"##"+new string('.',30);
        }
        var session=TwoRooms(source,destination);
        session.Projectiles.Add(new Projectile {Kind=ItemKind.Knife,Room=1,X=facing==Facing.Right?232:8,Y=160,
            Facing=facing,Phase=ProjectilePhase.Flying});
        for(int tick=0;tick<130;tick++)session.Tick(PlayerInput.None);
        Assert.Empty(session.Projectiles);
        var item=Assert.Single(session.Items,i=>i.Kind==ItemKind.Knife);
        Assert.Equal(2,item.Room);Assert.Equal(160,item.Y);
        Assert.Equal(facing==Facing.Right?224:16,item.X);
    }

    [Theory]
    [InlineData(ItemKind.Knife,Facing.Right)]
    [InlineData(ItemKind.Knife,Facing.Left)]
    [InlineData(ItemKind.Boomerang,Facing.Right)]
    [InlineData(ItemKind.Boomerang,Facing.Left)]
    public void OffscreenWeaponsProbeTheWallInTheirOwnRoom(ItemKind kind,Facing facing)
    {
        var session=TwoRooms(StageTestData.Box(),StageTestData.Box());
        var projectile=new Projectile {Kind=kind,Room=2,X=facing==Facing.Right?224:16,Y=160,
            Facing=facing,Phase=ProjectilePhase.Flying,Timer=20};
        session.Projectiles.Add(projectile);session.Tick(PlayerInput.None);
        Assert.Equal(ProjectilePhase.Waiting,projectile.Phase);
        Assert.Equal(2,projectile.Room);
    }

    [Fact]
    public void CrossingTheEdgeProbesTheDestinationOnThatSameTick()
    {
        var source=StageTestData.Box(); source[20]=source[21]=new string('.',32);
        var destination=StageTestData.Box();
        // At X=0 the native forward probe reads X+16, not the wall behind the weapon.
        destination[20]=destination[21]="####"+new string('.',26)+"##";
        var session=TwoRooms(source,destination);
        var projectile=new Projectile {Kind=ItemKind.Knife,Room=1,X=252,Y=160,
            Facing=Facing.Right,Phase=ProjectilePhase.Flying};
        session.Projectiles.Add(projectile);session.Tick(PlayerInput.None);
        Assert.Equal(2,projectile.Room);
        Assert.Equal(ProjectilePhase.Waiting,projectile.Phase);
    }

    [Fact]
    public void AWeaponFallingOutsideThePlayersRoomLandsOnThatRoomsFloor()
    {
        var session=TwoRooms(StageTestData.Box(),StageTestData.Box());
        session.Projectiles.Add(new Projectile {Kind=ItemKind.Knife,Room=2,X=104,Y=159,
            Phase=ProjectilePhase.Falling,Timer=4});
        session.Tick(PlayerInput.None);
        Assert.Empty(session.Projectiles);
        var item=Assert.Single(session.Items);
        Assert.Equal(2,item.Room);Assert.Equal(160,item.Y);
    }

    [Fact]
    public void FallingAcrossASingleRoomBoundaryWrapsToTheTopInsteadOfInventingAFloor()
    {
        var rows=StageTestData.Box();rows[22]=rows[23]=new string('.',32);
        var session=new StageSession(StageTestData.Stage(StageTestData.Json(new[]{rows})));
        var projectile=new Projectile {Kind=ItemKind.Knife,Room=1,X=104,Y=183,
            Phase=ProjectilePhase.Falling,Timer=4};
        session.Projectiles.Add(projectile);session.Tick(PlayerInput.None);
        Assert.Equal(1,projectile.Room);Assert.Equal(8,projectile.Y);
        Assert.Contains(projectile,session.Projectiles);Assert.Empty(session.Items);
    }

    [LocalRomFact("msx1")]
    public void StageThreeKnifeFlightMatchesTheMsx1CartridgeAcrossTheRoomBoundary()=>NativeStageThree("msx1",0xA297);

    [LocalRomFact("msx2")]
    public void StageThreeKnifeFlightMatchesTheMsx2CartridgeAcrossTheRoomBoundary()=>NativeStageThree("msx2_scc",0xA717);

    private static void NativeStageThree(string variant,ushort flyRoutine)
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KingsValley","roms","kv2_"+variant+".rom"));
        var session=new StageSession(StageLoader.Parse(RomStageJson.Build(rom,3)));
        session.Coffins.Clear();session.Enemies.Clear();session.Items.Clear();session.PlacePlayer(1,104,160);
        var projectile=new Projectile {Kind=ItemKind.Knife,Room=1,X=200,Y=160,Facing=Facing.Right,
            Phase=ProjectilePhase.Flying};session.Projectiles.Add(projectile);
        var machine=new MiniMsx(rom,RomMapper.KonamiScc);Assert.True(machine.Boot(2_000_000));
        RomStageState.Read(machine,RomStageState.ResolveStageLoader(rom),3);
        machine.SetBank(0x6000,1);machine.SetBank(0x8000,2);machine.SetBank(0xA000,3);
        // Isolate the native knife flight: IX=E300; skip animation/sound and enemy-hit flags.
        byte[] record={0x31,160,200,1,0,0,0,1};
        for(int i=0;i<record.Length;i++)machine.Write((ushort)(0xE300+i),record[i]);
        machine.Write(0xEDCD,0);machine.Write(0xE203,1);
        byte[] call={0xDD,0x21,0,0xE3,0xCD,(byte)flyRoutine,(byte)(flyRoutine>>8),0xC9};
        for(int i=0;i<call.Length;i++)machine.Write((ushort)(0xF100+i),call[i]);
        bool stopped=false;
        for(int tick=0;tick<100;tick++)
        {
            machine.CallAddress(0xF100,100_000);session.Tick(PlayerInput.None);
            Assert.Equal(machine.Memory(0xE303),projectile.Room);
            Assert.Equal(machine.Memory(0xE302),projectile.X);
            Assert.Equal(machine.Memory(0xE301),projectile.Y);
            bool nativeWaiting=(machine.Memory(0xE300)>>4)==4;
            Assert.Equal(nativeWaiting,projectile.Phase==ProjectilePhase.Waiting);
            if(nativeWaiting){stopped=true;break;}
        }
        Assert.True(stopped);Assert.Equal(2,projectile.Room);
        for(int tick=0;tick<30;tick++)session.Tick(PlayerInput.None);
        Assert.Empty(session.Projectiles);
        Assert.Contains(session.Items,i=>i.Kind==ItemKind.Knife && i.Room==2 && i.Y==160);
    }

    private static StageSession TwoRooms(string[] source,string[] destination)=>new(StageTestData.Stage(
        StageTestData.Json(new[]{source,destination},grid:"[[1,2,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0],[0,0,0,0,0,0,0,0]]")));
}
