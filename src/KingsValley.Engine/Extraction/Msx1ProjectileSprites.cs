#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Native knife and boomerang frames from the MSX1 E300-to-SAT routine.</summary>
internal static class Msx1ProjectileSprites
{
    public static IEnumerable<RomDirectExtractor.DirectSprite> Extract(byte[] rom, MiniMsx machine, CancellationToken token)
    {
        if(machine.Memory(0xF0F4)!=0) throw new InvalidDataException("The projectile recipe requires native MSX1 video mode.");
        byte[] patterns=(byte[])machine.Vram.Clone();
        int Offset(int address)=>2*8192+address-0x8000;
        for(int frame=0;frame<6;frame++)
        {
            token.ThrowIfCancellationRequested();
            for(int i=0;i<512;i++) machine.Write((ushort)(0xE300+i),0);
            int kind=frame<3?1:2;
            // State 2 is in flight. 8F0B copies raw Y/X, room, animation and kind into E810.
            machine.Write(0xE300,(byte)(0x20|kind));
            machine.Write(0xE301,63); machine.Write(0xE302,64);
            machine.Write(0xE303,machine.Memory(0xE243)); machine.Write(0xE304,(byte)frame);
            machine.CallAddress(0x4192,100_000);
            machine.CallAddress(0x8ECA,100_000);
            byte[] attributes=new byte[4];
            for(int i=0;i<4;i++) attributes[i]=machine.Memory((ushort)(0xE810+i));
            if(attributes[0]!=63 || attributes[1]!=64 || attributes[2]!=rom[Offset(0x8FAF+frame)] || attributes[3]!=rom[Offset(0x8FAC+kind)])
                throw new InvalidDataException("The native MSX1 projectile attributes did not match their ROM tables.");
            byte[] indices=Tms9918.DecodeSpriteRegion(patterns,machine.VdpRegisters,attributes,64,64,16,16),rgba=new byte[16*16*4];
            int visible=0;
            for(int i=0;i<indices.Length;i++)
            {
                var color=Tms9918.Palette[indices[i]];
                rgba[i*4]=color.R; rgba[i*4+1]=color.G; rgba[i*4+2]=color.B;
                if(indices[i]!=0) {rgba[i*4+3]=255;visible++;}
            }
            if(visible==0) throw new InvalidDataException($"Native MSX1 projectile frame {frame} has no visible pattern pixels.");
            yield return new($"proj_{4+frame*2:00}",16,16,rgba);
        }
    }
}
