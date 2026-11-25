#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Object states rendered by the MSX1 cartridge from its own tile-code tables.</summary>
internal static class Msx1ObjectSprites
{
    public static IEnumerable<RomDirectExtractor.DirectSprite> Extract(byte[] rom,MiniMsx machine,CancellationToken token)
    {
        if(machine.Memory(0xF0F4)!=0) throw new InvalidDataException("The object recipe requires native MSX1 video mode.");
        byte[] Crop(int width,int height)
        {
            byte[] frame=Tms9918.DecodeScreen2(machine.Vram,machine.VdpRegisters),rgba=new byte[width*height*4];
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                byte index=frame[(64+y)*256+64+x];var c=Tms9918.Palette[index];int offset=(y*width+x)*4;
                rgba[offset]=c.R;rgba[offset+1]=c.G;rgba[offset+2]=c.B;rgba[offset+3]=255;
            }
            return rgba;
        }
        for(int pyramid=1;pyramid<=6;pyramid++)
        {
            token.ThrowIfCancellationRequested();
            machine.Write(0xE241,(byte)pyramid);machine.Write(0xE242,(byte)(1+(pyramid-1)*10));machine.Write(0xE243,1);
            machine.CallAddress(RomStageState.ResolveStageLoader(rom),2_000_000);
            machine.CallAddress(0x4192,100_000);
            for(int frame=0;frame<3;frame++)
            {
                machine.Write(0xE2F1,64);machine.Write(0xE2F2,64);
                machine.Write(0xE2F3,machine.Memory(0xE243));machine.Write(0xE2F5,0);machine.Write(0xE2F6,(byte)frame);
                machine.CallAddress(0x8C14,100_000);
                int names=(machine.VdpRegisters[2]&15)<<10;
                for(int y=0;y<4;y++) for(int x=0;x<4;x++)
                    if(machine.Vram[names+(8+y)*32+8+x]!=rom[2*8192+0x9059+frame*16+y*4+x-0x8000])
                        throw new InvalidDataException("The native MSX1 exit-door name table did not match its ROM recipe.");
                yield return new($"door_f{frame}_p{pyramid}",32,32,Crop(32,32));
            }
            machine.CallRoutine(0x53D6,hl:0x9089,de:0x4040,bc:0x0202,maxInstructions:100_000);
            yield return new($"movable_block_p{pyramid}",16,16,Crop(16,16));
            foreach(var tile in new[]{(Name:"door_stud",Code:(int)rom[2*8192+0x8C72-0x8000]),
                (Name:"ladder",Code:(int)rom[0x5EF]),(Name:"ladder_alt",Code:(int)rom[0x5F3]),
                (Name:"soft",Code:(int)rom[0x573]),(Name:"wall",Code:(int)rom[0x577])})
            {
                machine.CallRoutine(0x53EF,a:(byte)tile.Code,de:0x4040,maxInstructions:100_000);
                byte[] pixels=Crop(8,8);
                // Four port/editor quadrant names refer to the same native terrain tile.
                if(tile.Name is "soft" or "wall")
                    for(int variant=0;variant<4;variant++) yield return new($"{tile.Name}_p{pyramid}_{variant}",8,8,pixels);
                else yield return new($"{tile.Name}_p{pyramid}",8,8,pixels);
            }
            foreach(var state in new[]{(Name:"coffin",Table:0x90C5),(Name:"coffin_ajar",Table:0x90CB),(Name:"coffin_open",Table:0x90D1)})
            {
                token.ThrowIfCancellationRequested();
                // AACE stamps these six row-major codes at actor Y - 8. 53D6 is its native tile-grid writer.
                machine.CallRoutine(0x53D6,hl:(ushort)state.Table,de:0x4040,bc:0x0302,maxInstructions:100_000);
                int names=(machine.VdpRegisters[2]&15)<<10;
                for(int y=0;y<3;y++) for(int x=0;x<2;x++)
                    if(machine.Vram[names+(8+y)*32+8+x]!=rom[2*8192+state.Table+y*2+x-0x8000])
                        throw new InvalidDataException("The native MSX1 coffin name table did not match its ROM recipe.");
                yield return new($"{state.Name}_p{pyramid}",16,24,Crop(16,24));
            }
            // The native digging renderer uses codes 6..23. Preserve black background pixels,
            // which the previous embedded-image path incorrectly treated as transparent.
            for(int code=6;code<24;code++)
            {
                token.ThrowIfCancellationRequested();
                machine.CallRoutine(0x53EF,a:(byte)code,de:0x4040,maxInstructions:100_000);
                yield return new($"crumble_{code}_p{pyramid}",8,8,Crop(8,8));
            }
        }
    }
}
