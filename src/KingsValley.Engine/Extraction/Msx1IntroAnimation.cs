#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KingsValley.Core.Screens;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Records the MSX1 intro's native sprite layer and positions at emulated vblank cadence.</summary>
public static class Msx1IntroAnimation
{
    public static void Write(byte[] rom,string destination,Action<string>? log=null,CancellationToken token=default)
    {
        var profile=RomExtractionProfiles.FindForRom(rom);
        if(profile?.GameId!="kings-valley-2" || profile.Variant!="msx1")throw new InvalidDataException("The intro recipe requires the verified MSX1 cartridge.");
        bool usesMainLoop=Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(rom))==RomFolderManager.Msx1FrsSha1;
        MiniMsx machine;
        if(usesMainLoop)
            machine=RomScreens.BootToIntro(rom,token);
        else
        {
            machine=new MiniMsx(rom,RomMapper.KonamiScc);machine.SetBiosByte(0x002B,0);
            if(!machine.Boot(2_000_000))throw new InvalidDataException("The native intro machine did not boot.");
            machine.CallAddress(0x4192);machine.CallAddress(0x6611,2_000_000);
            // Original RC-760 advances inside H.TIMI. Direct setup returns the main CPU
            // to a sentinel. FRS DynamicVsync instead needs its booted main loop intact.
            machine.MainRunning=false;
            machine.Write(0xE200,3);machine.Write(0xE201,6);machine.Write(0xE24A,0);
        }
        var animation=new NativeActorAnimation {FramesPerSecond=machine.FramesPerSecond};
        var poses=new List<byte[]>();var unique=new Dictionary<string,int>(StringComparer.Ordinal);
        bool completed=false;
        for(int frame=0;frame<2400;frame++)
        {
            token.ThrowIfCancellationRequested();machine.Frame();
            if(machine.Memory(0xE200)!=3 || machine.Memory(0xE201)!=6 || machine.Memory(0xE24A)!=0){completed=true;break;}
            int sat=(machine.VdpRegisters[5]&127)<<7;
            // Intro SAT 74A1 writes records 4..7, not the gameplay player's records 0..3.
            byte[] attributes=machine.Vram.AsSpan(sat+16,16).ToArray();
            byte[] layer=Tms9918.DecodeSpriteRegion(machine.Vram,machine.VdpRegisters,attributes,0,0,256,192);
            int left=256,top=192,right=0,bottom=0;
            for(int i=0;i<layer.Length;i++)if(layer[i]!=0)
            {int x=i%256,y=i/256;left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);}
            int pose=-1;
            if(left<right)
            {
                if(right-left>16 || bottom-top>32)throw new InvalidDataException("The native intro actor exceeded its sprite-strip cell.");
                var pixels=new byte[16*32];
                for(int y=top;y<bottom;y++)Array.Copy(layer,y*256+left,pixels,(y-top)*16,right-left);
                string key=Convert.ToHexString(pixels);
                if(!unique.TryGetValue(key,out pose)){pose=poses.Count;unique.Add(key,pose);poses.Add(pixels);}
            }
            animation.Frames.Add(new(left<right?left:0,left<right?top:0,pose,machine.Memory(0xE257)>=10?1:0));
        }
        if(!completed || !animation.Frames.Any(f=>f.Pose>=0) || !animation.Frames.Any(f=>f.Background==1))
            throw new InvalidDataException("The native intro did not complete its actor and message phases within the frame bound.");
        animation.PoseCount=poses.Count;
        byte[] strip=new byte[poses.Count*16*32];
        for(int pose=0;pose<poses.Count;pose++)for(int row=0;row<32;row++)
            Array.Copy(poses[pose],row*16,strip,row*poses.Count*16+pose*16,16);
        string screens=Path.Combine(destination,"Screens");Directory.CreateDirectory(screens);
        File.WriteAllBytes(Path.Combine(screens,"intro_vick.bin"),ScreenAssembler.Encode(poses.Count*16,32,RomScreens.TmsPalette,strip,hasAlpha:true,paletteModel:ScreenPaletteModel.Tms9918));
        animation.Save(Path.Combine(screens,"intro_vick.json"));
        log?.Invoke($"MSX1 intro: {animation.Frames.Count} native vblanks, {poses.Count} sprite poses, exact positions and visibility.");
    }
}
