#nullable enable
using System.Text.Json;
using KingsValley.Core.Extraction;
using KingsValley.Core.Screens;
using KingsValley.Engine.Assets;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Measured native addresses for the verified JoySNES Contest cartridge; no original-ROM bytes are substituted.</summary>
public static class ContestCartridge
{
    public const string Sha1="4a157e6e217f6af4ab3dec5d7dd5188e5cef7954";
    public static bool Is(byte[] rom)=>RomExtractionProfiles.FindForRom(rom)?.Variant==CartridgeVariants.Contest;
    public static void Select(MiniMsx machine,int stage,int pyramid=1)
    {
        if(stage is <1 or >20)throw new ArgumentOutOfRangeException(nameof(stage));
        machine.Write(0xE21C,(byte)((stage-1)/5));machine.Write(0xE21D,(byte)((stage-1)%5));
        machine.Write(0xF0F6,0);machine.Write(0xF0F7,0);
        machine.Write(0xE241,(byte)pyramid);machine.Write(0xE242,1);machine.Write(0xE243,1);
        machine.CallAddress(0x413B);machine.CallAddress(0x5A73,2_000_000);
    }
    public static RomLevel ReadLevel(MiniMsx machine,int stage)
    {
        int[][] grid=Enumerable.Range(0,8).Select(y=>Enumerable.Range(0,8).Select(x=>y<7?(int)machine.Memory((ushort)(0xE788+y*8+x)):0).ToArray()).ToArray();
        int count=grid.SelectMany(row=>row).Max();
        if(count is <1 or >8 || grid.SelectMany(row=>row).Count(x=>x>0)!=count)
            throw new InvalidDataException($"Invalid native Contest room grid for stage {stage}: max {count}, cells {grid.SelectMany(row=>row).Count(x=>x>0)}.");
        var rooms=new List<RomRoom>();
        for(int room=0;room<count;room++)
        {
            var rows=new string[24];
            for(int y=0;y<24;y++)rows[y]=new string(Enumerable.Range(0,32).Select(x=>
            {
                byte packed=machine.Memory((ushort)(0xE900+room*192+y*8+x/4));
                return ".?=#"[(packed>>(6-2*(x%4)))&3];
            }).ToArray());
            rooms.Add(new(room+1,rows));
        }
        var items=new List<RomItem>();
        for(int i=0;i<64;i++)
        {
            int a=0xE300+i*8;int kind=machine.Memory((ushort)a);if(kind==0)continue;
            if(kind>6)throw new InvalidDataException("Invalid Contest item record.");
            items.Add(new(kind,machine.Memory((ushort)(a+3)),machine.Memory((ushort)(a+1)),machine.Memory((ushort)(a+2))));
        }
        var golden=new List<RomLadder>();
        for(int i=0;i<16;i++)
        {
            int a=0xE7C0+i*4;int room=machine.Memory((ushort)a);if(room==0)continue;
            golden.Add(new(room,machine.Memory((ushort)(a+2))/8,machine.Memory((ushort)(a+1))/8,machine.Memory((ushort)(a+3))&31));
        }
        return new(){Stage=stage,Pyramid=1,RoomGrid=grid,Rooms=rooms,RoomCountInGrid=count,Items=items,GoldenLadders=golden,Music=0};
    }
    public static void WriteStages(byte[] rom,string directory,int count)
    {
        var machine=new MiniMsx(rom,RomMapper.KonamiScc);machine.Boot(2_000_000);
        Directory.CreateDirectory(directory);
        for(int stage=1;stage<=count;stage++)
        {
            Select(machine,stage);
            File.WriteAllText(Path.Combine(directory,$"stage{stage:00}.json"),RomStageJson.Build(ReadLevel(machine,stage),RomStageState.Dump(machine,stage),stage));
        }
    }
    public static void Prepare(Msx2VdpRunner machine,int stage,int pyramid=1)
    {
        machine.SetMemory(0xE21C,(byte)((stage-1)/5));machine.SetMemory(0xE21D,(byte)((stage-1)%5));
        machine.SetMemory(0xF0F6,0);machine.SetMemory(0xF0F7,0);
        machine.SetMemory(0xE241,(byte)pyramid);machine.SetMemory(0xE242,1);machine.SetMemory(0xE243,1);
        machine.Invoke(0x413B);machine.Invoke(0x5A67);
    }
    public static void WriteRooms(byte[] rom,string destination)
    {
        string rooms=Path.Combine(destination,"Rooms");Directory.CreateDirectory(rooms);
        var murals=new Dictionary<string,object>();var palettes=new Dictionary<string,object>();var animations=new Dictionary<string,object>();
        var reader=new RomDirectExtractor.RomReader(rom);
        for(int pyramid=1;pyramid<=6;pyramid++)
        {
            var machine=new Msx2VdpRunner(rom);machine.BootContest();Prepare(machine,1,pyramid);
            var blocks=new byte[8192];
            for(int tile=0;tile<256;tile++)for(int row=0;row<8;row++)
                Array.Copy(machine.Vram,0x8000+((tile/32)*8+row)*128+(tile%32)*4,blocks,tile*32+row*4,4);
            File.WriteAllBytes(Path.Combine(rooms,$"blocks_p{pyramid}.bin"),blocks);
            murals[pyramid.ToString()]=new{odd=RomDirectExtractor.MuralCodes(reader,pyramid,"odd"),even=RomDirectExtractor.MuralCodes(reader,pyramid,"even")};
            palettes[pyramid.ToString()]=machine.SnapshotScreen5().Palette.Select(c=>new[]{(int)c.R,c.G,c.B}).ToArray();
            int off=15*8192+0x16A7+pyramid*5;
            int color=rom[off],limit=rom[off+1],period=rom[off+2],pointer=rom[off+3]|rom[off+4]<<8;
            int[][] Ramp(int address,int length)=>Enumerable.Range(0,length).Select(i=>
            {int p=15*8192+(address&8191)+i*2;int word=rom[p]|rom[p+1]<<8;return new[]{((word>>4)&7)*255/7,((word>>8)&7)*255/7,(word&7)*255/7};}).ToArray();
            var slots=new List<object>{new{colour=color,limit,period,ramp=Ramp(pointer,limit)}};
            if(pyramid==4)slots.Add(new{colour=6,limit=2,period=5,ramp=Ramp(0xB6F1,2)});
            animations[pyramid.ToString()]=slots;
        }
        void Save(string name,object value)=>File.WriteAllText(Path.Combine(rooms,name+".json"),JsonSerializer.Serialize(value));
        Save("murals",murals);Save("palettes",palettes);Save("animations",animations);
        Save("decorations",Enumerable.Range(1,20).ToDictionary(n=>n.ToString(),n=>Array.Empty<object>()));
    }
    public static void WriteScreens(byte[] rom,string destination)
    {
        void Save(string name,ScreenData screen)
        {
            string path=Path.Combine(destination,name+".bin");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path,ScreenAssembler.Encode(screen.Width,screen.Height,screen.Palette,screen.Indices,screen.HasAlpha));
        }
        var title=new Msx2VdpRunner(rom);title.BootContest();bool logo=false,finished=false;
        for(int tick=0;tick<900;tick++)
        {
            title.Invoke(0x44BC);
            if(!logo && title.Memory(0xE200)==0 && title.Memory(0xE201)==3){Save("Screens/konami",title.SnapshotScreen5());logo=true;}
            if(title.Memory(0xE200)==1 && title.Memory(0xE201)==0){finished=true;break;}
        }
        if(!logo || !finished)throw new InvalidDataException("Contest title sequence did not complete.");
        Save("Screens/title",title.SnapshotScreen5());
        title.SetMemory(0xE200,3);title.SetMemory(0xE201,0);
        for(int tick=0;tick<8;tick++)title.Invoke(0x44BC);
        Save("Screens/start_select",title.SnapshotScreen5());
        // Native publication, author and visual-theme menus. Each complete frame includes
        // the cartridge's own selection marker; no authored cursor or translated art is used.
        for(int group=0;group<4;group++)
        {
            var menu=new Msx2VdpRunner(rom);menu.BootContest();
            menu.SetMemory(0xF0F6,0);menu.SetMemory(0xF0F7,0);
            menu.SetMemory(0xE21C,(byte)group);menu.SetMemory(0xE21D,0);
            menu.Invoke(0x4191);menu.Invoke(0x5270);menu.Invoke(0x41E1);
            menu.SetMemory(0xE21E,0);menu.Invoke(0x5D12);
            Save($"Screens/contest_publication_{group}",menu.SnapshotScreen5());
            menu.Invoke(0x51AC);menu.Invoke(0x5AE9);menu.Invoke(0x4AF3);menu.Invoke(0x5389);menu.Invoke(0x5DD1);
            for(int entry=0;entry<5;entry++)
            {
                menu.SetMemory(0xE21D,(byte)entry);menu.SetMemory(0xE21F,(byte)Math.Max(0,entry-1));menu.Invoke(0x5D7D);
                Save($"Screens/contest_author_{group}_{entry}",menu.SnapshotScreen5());
            }
        }
        var themes=new Msx2VdpRunner(rom);themes.BootContest();themes.Invoke(0x4A60);themes.Invoke(0x5888);
        for(int pyramid=1;pyramid<=6;pyramid++)
        {
            themes.SetMemory(0xE241,(byte)pyramid);themes.Invoke(0x59A9);
            Save($"Screens/contest_theme_{pyramid}",themes.SnapshotScreen5());
        }
        Save("Screens/contest_clear",CaptureCompletion(rom));
    }
    /// <summary>Contest 47CF: native congratulation text after the 48-call closing curtain.</summary>
    public static ScreenData CaptureCompletion(byte[] rom)
    {
        var native=new Msx2VdpRunner(rom);native.BootContest();Prepare(native,1);
        native.SetMemory(0xE243,3);native.Invoke(0x434F);
        native.Invoke(0x5679);native.Invoke(0x4D99,hl:0x5C1A);
        native.SetMemory(0xE200,9);native.SetMemory(0xE201,0);
        native.SetMemory(0xE257,3);native.SetMemory(0xE258,0);native.SetMemory(0xE259,4);
        for(int tick=0;tick<48;tick++)native.Invoke(0x44BC);
        if(native.Memory(0xE200)!=9 || native.Memory(0xE201)!=1 || native.Memory(0xE204)!=180)
            throw new InvalidDataException("The Contest cartridge did not reach its native completion screen.");
        return native.SnapshotScreen5();
    }
}
