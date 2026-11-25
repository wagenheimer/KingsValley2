#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Engine.Extraction;

/// <summary>Executes native actor row/trap writers on an isolated surface for every mechanism state.</summary>
public static class Msx2MechanismFrames
{
    public static void Write(byte[] rom,string destination,Action<string>? log=null,CancellationToken token=default)
    {
        var profile=RomExtractionProfiles.FindForRom(rom);
        if(profile?.GameId!="kings-valley-2" || profile.Variant!="msx2")throw new InvalidDataException("The MSX2 mechanism recipe requires the verified MSX2 cartridge.");
        var machine=new Msx2VdpRunner(rom);machine.Boot();var frames=new Dictionary<string,byte[]>();
        for(int pyramid=1;pyramid<=6;pyramid++)
        {
            token.ThrowIfCancellationRequested();machine.PrepareAndPaint((pyramid-1)*10+1);
            foreach(int type in new[]{1,2,4})
            for(int direction=0;direction<(type==2?2:1);direction++)
            for(int phase=0;phase<(type==4?1:4);phase++)
            {
                token.ThrowIfCancellationRequested();
                for(int i=0;i<256;i++)machine.SetMemory((ushort)(0xE600+i),0);
                machine.SetMemory(0xE600,(byte)type);machine.SetMemory(0xE602,64);machine.SetMemory(0xE603,64);
                machine.SetMemory(0xE604,1);machine.SetMemory(0xE605,(byte)phase);
                machine.SetMemory(0xE607,(byte)(direction*2));machine.SetMemory(0xE608,3);
                machine.Invoke(0x418A);
                // Native transparent tile copies must land on an empty capture surface;
                // otherwise untouched pixels would accidentally include the room's mural.
                machine.Invoke(0x4FED,accumulator:0,hl:0x4040,de:0,bc:0x2018);
                // 644A/BB53 restore the room mural before drawing. Enter their native
                // row-writing tail directly to retain transparent pixels as zero.
                if(type==4)machine.Invoke(0xBDC0,ix:0xE600);
                else
                {
                    ushort top=(ushort)(type==1?0x9374:0x937C+direction*24);
                    machine.Invoke(0xBB63,hl:top,de:(ushort)(top+(type==1?0:8)),bc:(ushort)(top+(type==1?0:16)),ix:0xE600);
                }
                int width=type==4?32:16,height=type==4?8:24;
                var screen=machine.SnapshotScreen5();var pixels=new byte[width*height];
                for(int row=0;row<height;row++)Array.Copy(screen.Indices,(64+row)*256+64,pixels,row*width,width);
                if(!pixels.Any(p=>p>=2))throw new InvalidDataException("The native MSX2 mechanism frame was blank.");
                string name=type==4?"mechanism_t4.bin":type==1?$"mechanism_t1_p{phase}.bin":$"mechanism_t2_d{direction}_p{phase}.bin";
                byte[] packed=Packed4Bpp.Pack(pixels);
                frames[$"p{pyramid}/"+name]=packed;
            }
        }
        string rooms=Path.Combine(destination,"Rooms");Directory.CreateDirectory(rooms);
        foreach(var frame in frames)
        {
            string path=Path.Combine(rooms,"Mechanisms",frame.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path,frame.Value);
        }
        log?.Invoke("MSX2 mechanisms: 13 native states extracted independently for each of six pyramids (78 frames).");
    }
}
