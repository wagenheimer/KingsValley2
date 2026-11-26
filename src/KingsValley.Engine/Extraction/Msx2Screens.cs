#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using KingsValley.Core.Screens;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Engine.Extraction;

/// <summary>Fixed-screen recipes executed by the verified MSX2 cartridge, without an external emulator.</summary>
public static class Msx2Screens
{
    public static void WriteSupported(byte[] rom,string directory,Action<string>? log=null,CancellationToken token=default)
    {
        var identity=RomExtractionProfiles.FindForRom(rom);
        if(identity?.GameId!="kings-valley-2" || identity.Variant!="msx2")throw new InvalidDataException("The MSX2 screen recipe requires the verified MSX2 cartridge.");
        void Save(string relative,ScreenData image)
        {
            token.ThrowIfCancellationRequested();
            if(!image.Indices.Any(p=>p>=2))throw new InvalidDataException("Native MSX2 content was blank: "+relative);
            string path=Path.Combine(directory,relative+".bin");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path,ScreenAssembler.Encode(image.Width,image.Height,image.Palette,image.Indices,hasAlpha:image.HasAlpha));
            log?.Invoke("MSX2 native screen: "+relative);
        }
        ScreenData Crop(Msx2VdpRunner machine,int x,int y,int width,int height)
        {
            var screen=machine.SnapshotScreen5();var pixels=new byte[width*height];
            for(int row=0;row<height;row++)Array.Copy(screen.Indices,(y+row)*256+x,pixels,row*width,width);
            return new(width,height,false,screen.Palette,pixels);
        }
        ScreenData Sprite(Msx2VdpRunner machine,int count,int x,int y,int width,int height)
        {
            byte[] attributes=Enumerable.Range(0,count*4).Select(i=>machine.Memory((ushort)(0xE800+i))).ToArray();
            byte[] colors=Enumerable.Range(0,count*16).Select(i=>machine.Memory((ushort)(0xD200+i))).ToArray();
            var pixels=V9938Sprites.DecodeRegion(machine.Vram,machine.VdpRegisters,attributes,colors,x,y,width,height);
            return new(width,height,true,machine.SnapshotScreen5().Palette,pixels);
        }
        var title=new Msx2VdpRunner(Msx2ExecutionImage.For(rom));title.BootVerified();
        bool logo=false,finished=false;
        for(int tick=0;tick<650;tick++)
        {
            token.ThrowIfCancellationRequested();title.Invoke(0x46DA);
            if(!logo && title.Memory(0xE200)==0 && title.Memory(0xE201)==3){Save("Screens/konami",title.SnapshotScreen5());logo=true;}
            if(title.Memory(0xE200)==1 && title.Memory(0xE201)==0){finished=true;break;}
        }
        if(!logo || !finished)throw new InvalidDataException("The MSX2 cartridge did not complete its title sequence.");
        // Native 5F8D unconditionally draws the Japanese title. This cartridge has no regional
        // title selection at BIOS 002B; do not fabricate title_en or borrow the MSX1 artwork.
        title.Invoke(0x4D75,de:0x388C,bc:0);title.Invoke(0x4D75,de:0x389C,bc:0);
        Save("Screens/title",title.SnapshotScreen5());
        title.Invoke(0x4D75,de:0x388C,bc:255);Save("Art/Native/title_cursor",Crop(title,56,140,16,8));
        title.Invoke(0x677E);Save("Screens/start_select",title.SnapshotScreen5());
        title.SetMemory(0xE207,0);title.Invoke(0x6794);
        Save("Art/Native/arrow",Sprite(title,1,80,81,16,16));

        var world=new Msx2VdpRunner(Msx2ExecutionImage.For(rom));world.BootVerified();world.SetMemory(0xE241,1);
        world.Invoke(0x6F84);Save("Screens/world_map",world.SnapshotScreen5());
        var intro=new Msx2VdpRunner(Msx2ExecutionImage.For(rom));intro.BootVerified();intro.Invoke(0x5BEB);
        intro.Invoke(0x6807);Save("Screens/pyramid",intro.SnapshotScreen5());
        intro.Invoke(0x6945);Save("Screens/intro_msg",intro.SnapshotScreen5());

        // Hidden-room UIs inherit unchanged palette entries (including white 11)
        // from gameplay. Their own palette list intentionally updates only a subset.
        var ui=new Msx2VdpRunner(Msx2ExecutionImage.For(rom));ui.BootVerified();ui.PrepareAndPaint(1);ui.Invoke(0x5BEB);
        ui.Invoke(0x41B5);ui.SetMemory(0xEF10,1);ui.SetMemory(0xEF11,0);ui.SetMemory(0xEF12,0);
        ui.Invoke(0xB534);Save("Screens/sound_select",ui.SnapshotScreen5());
        ui.Invoke(0x41B5);ui.Invoke(0xB534);
        int handX=ui.Memory(0xE801),handY=ui.Memory(0xE800)+1;
        Save("Screens/sound_select_cursor",Sprite(ui,4,handX,handY,16,32));
        ui.Invoke(0x41B5);ui.SetMemory(0xEF10,2);ui.SetMemory(0xEF11,0);ui.SetMemory(0xE20C,0);
        ui.Invoke(0xB534);Save("Screens/puzzle",ui.SnapshotScreen5());
        for(int piece=0;piece<25;piece++)
        {
            token.ThrowIfCancellationRequested();ui.Invoke(0xB7EC,accumulator:0,de:(ushort)(((88+piece%5*16)<<8)|(48+piece/5*16)));
        }
        Save("Screens/puzzle_bg",ui.SnapshotScreen5());
        for(int piece=0;piece<25;piece++)
        {
            token.ThrowIfCancellationRequested();ui.Invoke(0xB7EC,accumulator:(byte)((piece+1)%25),de:(ushort)(((88+piece%5*16)<<8)|(48+piece/5*16)));
        }
        Save("Screens/puzzle_tiles",Crop(ui,88,48,80,80));
    }
}
