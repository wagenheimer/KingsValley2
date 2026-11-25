#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Runs the MSX1 cartridge's single-plane enemy SAT builder against its live pattern table.</summary>
internal static class Msx1EnemySprites
{
    public static IEnumerable<RomDirectExtractor.DirectSprite> Extract(byte[] rom, MiniMsx machine, CancellationToken token)
    {
        if(machine.Memory(0xF0F4)!=0) throw new InvalidDataException("The enemy recipe requires native MSX1 video mode.");
        byte[] patterns=(byte[])machine.Vram.Clone();
        // AA79 indexes AA83 by enemy kind. The MSX2-only branches at 6429,
        // B051 and B440 must not introduce MSX2 vanish/statue color families here.
        for(int type=1;type<=4;type++) for(int frame=0;frame<26;frame++)
        {
            token.ThrowIfCancellationRequested();
            for(int i=0;i<256;i++) machine.Write((ushort)(0xE500+i),0);
            byte color=rom[3*8192+0xAA83+type-0xA000];
            machine.Write(0xE500,(byte)type);
            machine.Write(0xE503,64); machine.Write(0xE505,64);
            machine.Write(0xE510,machine.Memory(0xE243));
            machine.Write(0xE511,(byte)frame); machine.Write(0xE51E,color);
            machine.CallAddress(0x4192,100_000);
            machine.CallAddress(0x646E,100_000);
            byte[] attributes=new byte[4];
            for(int i=0;i<4;i++) attributes[i]=machine.Memory((ushort)(0xE840+i));
            int expected=rom[13*8192+0xBECF+frame-0xA000];
            if(attributes[0]!=63 || attributes[1]!=64 || attributes[2]!=expected || attributes[3]!=color)
                throw new InvalidDataException("The native MSX1 enemy attribute table did not match its ROM recipe.");
            byte[] indices=Tms9918.DecodeSpriteRegion(patterns,machine.VdpRegisters,attributes,64,64,16,16), rgba=new byte[16*16*4];
            int visible=0;
            for(int y=0;y<16;y++) for(int x=0;x<16;x++)
            {
                byte index=indices[y*16+x];
                var c=Tms9918.Palette[index]; int offset=(y*16+x)*4;
                rgba[offset]=c.R; rgba[offset+1]=c.G; rgba[offset+2]=c.B;
                if(index!=0) { rgba[offset+3]=255; visible++; }
            }
            if(visible==0) throw new InvalidDataException($"Native MSX1 enemy frame {frame} has no visible pattern pixels.");
            yield return new($"enemy_t{type}_{frame:00}",16,16,rgba);
        }
    }
}
