#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Engine.Extraction;

/// <summary>Native color animation streams and the exact clean-room cells which reference them.</summary>
public sealed class Msx1PatternAnimations
{
    private sealed record Animation(int period,int base_pattern,int pattern_count,int[][][] frames);
    private sealed record Cell(int x,int y,string type,int offset,int[] pat,int[] col);
    private readonly Dictionary<string,Animation> _pyramids=new(StringComparer.Ordinal);
    private readonly Dictionary<string,List<Cell>> _rooms=new(StringComparer.Ordinal);
    private Animation _torch=null!;

    public static Msx1PatternAnimations Create(byte[] rom)
    {
        var identity=RomExtractionProfiles.FindForRom(rom);
        if(identity?.GameId!="kings-valley-2" || identity.Variant!="msx1")
            throw new InvalidDataException("The MSX1 pattern animation recipe requires the verified MSX1 cartridge.");
        var result=new Msx1PatternAnimations();
        int Word(int offset)=>rom[offset]|rom[offset+1]<<8;
        int[][][] Frames(int table,int count)
        {
            var frames=new int[count][][];
            for(int frame=0;frame<count;frame++)
            {
                int pointer=Word(table+frame*2);
                if(pointer<0x6000 || pointer>=0xA000) throw new InvalidDataException("Invalid native animation stream pointer.");
                int offset=pointer<0x8000?7*8192+pointer-0x6000:8*8192+pointer-0x8000;
                byte[] colors=KonamiRle.DecodeBounded(rom,offset,128);
                if(colors.Length==0 || colors.Length%8!=0) throw new InvalidDataException("Native color frames require complete eight-row patterns.");
                frames[frame]=Enumerable.Range(0,colors.Length/8).Select(p=>colors.AsSpan(p*8,8).ToArray().Select(b=>(int)b).ToArray()).ToArray();
            }
            if(frames.Any(f=>f.Length!=frames[0].Length)) throw new InvalidDataException("Native animation frames have inconsistent pattern counts.");
            return frames;
        }
        for(int pyramid=1;pyramid<=6;pyramid++)
        {
            int table=0x1936+pyramid*10;
            var frames=Frames(table+2,4);
            result._pyramids[pyramid.ToString(System.Globalization.CultureInfo.InvariantCulture)]=new(rom[table+1],rom[table],frames[0].Length,frames);
        }
        // 591A masks the counter by 3; 5920 masks the frame by 7; 592A targets color byte 04C0.
        var torch=Frames(0x1930,rom[0x1921]+1);
        result._torch=new(rom[0x191B]+1,Word(0x192B)/8,torch[0].Length,torch);
        return result;
    }

    public void CaptureRoom(byte[] vram,byte[] registers,int stage,int room)
    {
        if(stage<1 || stage>60 || room<1 || room>99) throw new ArgumentOutOfRangeException(nameof(stage));
        if(vram.Length<16384 || registers.Length<8) throw new ArgumentException("Room animation capture requires native SCREEN 2 tables.");
        var eye=_pyramids[((stage-1)/10+1).ToString(System.Globalization.CultureInfo.InvariantCulture)];
        int names=(registers[2]&15)<<10;
        int patternBase=(registers[4]&4)<<11,colorBase=(registers[3]&128)<<6;
        int patternMask=((registers[4]&3)<<11)|((registers[3]&31)<<6)|63;
        int colorMask=((registers[3]&127)<<6)|63;
        var cells=new List<Cell>();
        for(int y=0;y<24;y++) for(int x=0;x<32;x++)
        {
            int code=vram[names+y*32+x];
            string type; int offset;
            if(code>=eye.base_pattern && code<eye.base_pattern+eye.pattern_count) {type="eye";offset=code-eye.base_pattern;}
            else if(code>=_torch.base_pattern && code<_torch.base_pattern+_torch.pattern_count) {type="torch";offset=code-_torch.base_pattern;}
            else continue;
            int[] pattern=new int[8],color=new int[8];
            for(int row=0;row<8;row++)
            {
                int index=(y/8)*2048+code*8+row;
                pattern[row]=vram[patternBase|(index&patternMask)];
                color[row]=vram[colorBase|(index&colorMask)];
            }
            cells.Add(new(x,y,type,offset,pattern,color));
        }
        _rooms[$"s{stage:00}_r{room:00}"]=cells;
    }

    public void Save(string destination)
    {
        string rooms=Path.Combine(destination,"Rooms"); Directory.CreateDirectory(rooms);
        File.WriteAllText(Path.Combine(rooms,"animations.json"),JsonSerializer.Serialize(new {pyramids=_pyramids,torch=_torch,rooms=_rooms},new JsonSerializerOptions{WriteIndented=true}));
    }

    public static void ValidateFile(string path,IEnumerable<string> expectedRooms)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        var root=json.RootElement;
        var pyramids=root.GetProperty("pyramids");
        if(pyramids.EnumerateObject().Count()!=6) throw new InvalidDataException("Native animation data must cover six pyramids.");
        int ValidateAnimation(JsonElement animation,int frameCount)
        {
            int period=animation.GetProperty("period").GetInt32(),first=animation.GetProperty("base_pattern").GetInt32(),count=animation.GetProperty("pattern_count").GetInt32();
            if(period<1 || period>255 || first<0 || count<1 || count>16 || first+count>256)
                throw new InvalidDataException("Invalid native animation dimensions or timing.");
            var frames=animation.GetProperty("frames");
            if(frames.GetArrayLength()!=frameCount) throw new InvalidDataException("Native animation frame inventory is incomplete.");
            foreach(var frame in frames.EnumerateArray())
            {
                if(frame.GetArrayLength()!=count) throw new InvalidDataException("Native animation pattern count does not match its frames.");
                foreach(var pattern in frame.EnumerateArray()) ValidateRows(pattern);
            }
            return count;
        }
        static void ValidateRows(JsonElement rows)
        {
            if(rows.GetArrayLength()!=8 || rows.EnumerateArray().Any(v=>!v.TryGetInt32(out int b) || b<0 || b>255))
                throw new InvalidDataException("Native pattern/color data requires eight valid row bytes.");
        }
        int[] counts=new int[6];
        for(int p=1;p<=6;p++) counts[p-1]=ValidateAnimation(pyramids.GetProperty(p.ToString(System.Globalization.CultureInfo.InvariantCulture)),4);
        int torchCount=ValidateAnimation(root.GetProperty("torch"),8);
        var rooms=root.GetProperty("rooms");
        foreach(string key in expectedRooms)
            if(!rooms.TryGetProperty(key,out _)) throw new InvalidDataException("Native animation cell inventory is missing room "+key);
        foreach(var room in rooms.EnumerateObject())
        {
            if(room.Name.Length!=7 || room.Name[0]!='s' || room.Name.Substring(3,2)!="_r" ||
                !int.TryParse(room.Name.AsSpan(1,2),out int stage) || stage<1 || stage>60 ||
                !int.TryParse(room.Name.AsSpan(5,2),out int roomNumber) || roomNumber<1)
                throw new InvalidDataException("Invalid native animation room key.");
            var positions=new HashSet<int>();
            foreach(var cell in room.Value.EnumerateArray())
            {
                int x=cell.GetProperty("x").GetInt32(),y=cell.GetProperty("y").GetInt32(),offset=cell.GetProperty("offset").GetInt32();
                string? type=cell.GetProperty("type").GetString();
                int count=type=="eye"?counts[(stage-1)/10]:type=="torch"?torchCount:0;
                if(x<0 || x>=32 || y<0 || y>=24 || offset<0 || offset>=count || !positions.Add(y*32+x))
                    throw new InvalidDataException("Invalid or duplicate native animated cell.");
                ValidateRows(cell.GetProperty("pat")); ValidateRows(cell.GetProperty("col"));
            }
        }
    }
}
