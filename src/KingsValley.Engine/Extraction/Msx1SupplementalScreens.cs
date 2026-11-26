#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using KingsValley.Core.Screens;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Additional MSX1 screen recipes executed through the verified cartridge's native routines.</summary>
public static class Msx1SupplementalScreens
{
    public static void Write(byte[] rom,string destination,Action<string>? log=null,CancellationToken token=default)
    {
        var identity=RomExtractionProfiles.FindForRom(rom);
        if(identity?.GameId!="kings-valley-2" || identity.Variant!="msx1") throw new InvalidDataException("The supplemental screen recipe requires the verified MSX1 cartridge.");
        void Save(string relative,int width,int height,byte[] pixels,bool alpha=false)
        {
            token.ThrowIfCancellationRequested();
            if(!pixels.Any(p=>p>=2)) throw new InvalidDataException("The native screen recipe produced blank content: "+relative);
            string path=Path.Combine(destination,relative+".bin");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path,ScreenAssembler.Encode(width,height,RomScreens.TmsPalette,pixels,hasAlpha:alpha,paletteModel:ScreenPaletteModel.Tms9918));
            log?.Invoke("MSX1 native screen: "+relative);
        }
        void Snapshot(string name,MiniMsx machine)=>Save("Screens/"+name,256,192,Tms9918.DecodeScreen2(machine.Vram,machine.VdpRegisters,resolveBackdrop:true));
        byte[] Crop(MiniMsx machine,int x,int y,int width,int height)
        {
            byte[] frame=Tms9918.DecodeScreen2(machine.Vram,machine.VdpRegisters,resolveBackdrop:true),pixels=new byte[width*height];
            for(int row=0;row<height;row++) Array.Copy(frame,(y+row)*256+x,pixels,row*width,width);
            return pixels;
        }
        var machine=RomScreens.BootToGameplay(rom,token);
        // Native title initialization and 4D62 select GAME using glyphs DC/DD at (56,136).
        machine.CallAddress(0x4192);machine.CallAddress(0x5D84,2_000_000);machine.CallAddress(0x5DA1,2_000_000);
        machine.Write(0xE222,0);machine.CallRoutine(0x4D62,bc:255);
        Save("Art/Native/title_cursor",16,8,Crop(machine,56,136,16,8));

        machine.CallAddress(0x6588,500_000);Snapshot("start_select",machine);
        machine.CallRoutine(0x65CF,hl:0xE24B);
        byte[] attributes=Enumerable.Range(0,4).Select(i=>machine.Memory((ushort)(0xE800+i))).ToArray();
        Save("Art/Native/arrow",16,16,Tms9918.DecodeSpriteRegion(machine.Vram,machine.VdpRegisters,attributes,80,81,16,16),true);

        machine.Write(0xE241,1);machine.CallAddress(0x6DEE,2_000_000);Snapshot("world_map",machine);
        // The map installs its own shifted alphabet. Start the following UI recipes from
        // a fresh native gameplay context so their normal-font strings keep their encoding.
        machine=RomScreens.BootToGameplay(rom,token);
        machine.CallAddress(0x6611,2_000_000);machine.CallAddress(0x6752,2_000_000);Snapshot("intro_msg",machine);

        // B530 supplies IX=EF10 and dispatches the native sound/puzzle initialization.
        machine.CallAddress(0x41BD);machine.Write(0xEF10,1);machine.Write(0xEF11,0);machine.Write(0xEF12,0);machine.Write(0xE20C,0);
        machine.CallAddress(0xB530,2_000_000);Snapshot("sound_select",machine);
        machine.CallAddress(0x41BD);machine.CallAddress(0xB530,500_000);
        attributes=Enumerable.Range(0,24).Select(i=>machine.Memory((ushort)(0xE800+i))).ToArray();
        Save("Screens/sound_select_cursor",16,32,Tms9918.DecodeSpriteRegion(machine.Vram,machine.VdpRegisters,attributes,attributes[1],attributes[0]+1,16,32),true);

        machine.CallAddress(0x41BD);machine.Write(0xEF10,2);machine.Write(0xEF11,0);machine.Write(0xE20C,0);
        machine.CallAddress(0xB530,2_000_000);Snapshot("puzzle",machine);
        for(int piece=0;piece<25;piece++)
        {
            token.ThrowIfCancellationRequested();
            machine.CallRoutine(0xB7E8,a:0,de:(ushort)(((88+piece%5*16)<<8)|(48+piece/5*16)));
        }
        Snapshot("puzzle_bg",machine);
        for(int piece=0;piece<25;piece++)
        {
            token.ThrowIfCancellationRequested();
            // The port's sheet uses slots 0..23 for native ids 1..24, and slot 24 for native id 0.
            machine.CallRoutine(0xB7E8,a:(byte)((piece+1)%25),de:(ushort)(((88+piece%5*16)<<8)|(48+piece/5*16)));
        }
        Save("Screens/puzzle_tiles",80,80,Crop(machine,88,48,80,80));

        var logo=CartridgeMachines.Create(rom);logo.SetBiosByte(0x002B,0);
        if(!logo.Boot(2_000_000)) throw new InvalidDataException("The MSX1 logo machine did not boot.");
        bool complete=false;
        for(int tick=0;tick<650;tick++)
        {
            token.ThrowIfCancellationRequested();logo.CallAddress(0x46DB,200_000);
            if(logo.Memory(0xE200)==0 && logo.Memory(0xE201)==3 && logo.Memory(0xE20F)==6)
            {Snapshot("konami",logo);complete=true;break;}
        }
        if(!complete) throw new InvalidDataException("The MSX1 cartridge did not complete its native Konami logo reveal.");
    }
}
