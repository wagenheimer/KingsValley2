#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Captures mechanisms through the verified MSX1 cartridge's native tile drawing routines.</summary>
public static class Msx1MechanismFrames
{
    public static void Write(byte[] rom,string destination,Action<string>? log=null,CancellationToken token=default)
    {
        var identity=RomExtractionProfiles.FindForRom(rom);
        if(identity?.GameId!="kings-valley-2" || identity.Variant!="msx1")
            throw new InvalidDataException("The MSX1 mechanism recipe requires the verified MSX1 cartridge.");
        token.ThrowIfCancellationRequested();
        var machine=RomScreens.BootToGameplay(rom,token);
        var frames=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        // Runtime files are shared by the six pyramids. Verify that the cartridge really uses
        // the same pixels in each one before writing that shared representation.
        for(int pyramid=1;pyramid<=6;pyramid++)
        {
            machine.Write(0xE241,(byte)pyramid); machine.Write(0xE242,(byte)(1+(pyramid-1)*10));
            machine.Write(0xE243,1);
            machine.CallAddress(RomStageState.ResolveStageLoader(rom),2_000_000);
            for(int type=1;type<=4;type++)
            {
                if(type==3) continue; // Type 3 collapses terrain; it has no independent frame file.
                for(int direction=0;direction<(type==2?2:1);direction++)
                for(int phase=0;phase<(type==4?1:4);phase++)
                {
                    token.ThrowIfCancellationRequested();
                    for(int i=0;i<256;i++) machine.Write((ushort)(0xE600+i),0);
                    machine.Write(0xE600,(byte)type);
                    machine.Write(0xE602,64); machine.Write(0xE603,64);
                    machine.Write(0xE604,machine.Memory(0xE243));
                    machine.Write(0xE605,(byte)phase); machine.Write(0xE607,(byte)(direction*2));
                    machine.Write(0xE608,3);
                    machine.CallAddress(0x4192,100_000);
                    machine.CallAddress(0x6254,500_000);
                    int width=type==4?32:16,height=type==4?8:24;
                    int names=(machine.VdpRegisters[2]&15)<<10;
                    // Verify native table selection before interpreting any pixels. Table pairs are
                    // horizontal, while the renderer advances vertically through top/middle/bottom.
                    for(int row=0;row<height/8;row++) for(int col=0;col<width/8;col++)
                    {
                        int table=type==1?0x908D:type==2?0x9095+direction*24+row*8:0;
                        byte expected=type==4?(byte)0x61:rom[2*8192+table+phase*2+col-0x8000];
                        if(machine.Vram[names+(8+row)*32+8+col]!=expected)
                            throw new InvalidDataException("The native MSX1 mechanism name table did not match its ROM recipe.");
                    }
                    byte[] screen=Tms9918.DecodeScreen2(machine.Vram,machine.VdpRegisters),pixels=new byte[width*height];
                    for(int row=0;row<height;row++) Array.Copy(screen,(64+row)*256+64,pixels,row*width,width);
                    if(!pixels.Any(p=>p>=2)) throw new InvalidDataException("The native MSX1 mechanism frame is blank.");
                    string name=type==4?"mechanism_t4.bin":type==1?$"mechanism_t1_p{phase}.bin":$"mechanism_t2_d{direction}_p{phase}.bin";
                    byte[] packed=Packed4Bpp.Pack(pixels);
                    if(frames.TryGetValue(name,out var first) && !first.AsSpan().SequenceEqual(packed))
                        throw new NotSupportedException("MSX1 mechanism pixels differ between pyramids; per-pyramid runtime frames are required.");
                    frames[name]=packed;
                }
            }
        }
        string rooms=Path.Combine(destination,"Rooms"); Directory.CreateDirectory(rooms);
        foreach(var frame in frames) File.WriteAllBytes(Path.Combine(rooms,frame.Key),frame.Value);
        log?.Invoke("MSX1 mechanisms: 13 native frames verified across all six pyramids.");
    }
}
