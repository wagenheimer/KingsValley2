using KingsValley.Core.Screens;
using KingsValley.Engine.Extraction;
using KingsValley.Engine.Graphics;
using Wagenheimer.MsxAudio.Extraction;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class Msx1IntroAnimationTests
{
    [LocalRomFact("msx1")]
    public void RecordedIntroReplaysTheNativeSpritePixelsAtEveryVblank()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx1.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-intro-trace-"+Guid.NewGuid().ToString("N"));
        try
        {
            Msx1IntroAnimation.Write(rom,root);
            var animation=NativeActorAnimation.Load(Path.Combine(root,"Screens","intro_vick.json"));
            var strip=ScreenAssembler.DecodeFile(Path.Combine(root,"Screens","intro_vick.bin"));
            Assert.Equal(60,animation.FramesPerSecond);Assert.InRange(animation.Frames.Count,400,2400);
            var machine=new MiniMsx(rom,RomMapper.KonamiScc);machine.SetBiosByte(0x002B,0);Assert.True(machine.Boot(2_000_000));
            machine.Write(0xE200,3);machine.Write(0xE201,5);machine.Write(0xE24A,0);
            machine.CallAddress(0x4869,2_000_000); // Native parent state enters the intro and advances to substate 6.
            machine.MainRunning=false;
            foreach(var frame in animation.Frames)
            {
                machine.Frame();int sat=(machine.VdpRegisters[5]&127)<<7;
                int state=machine.Memory(0xE257);
                string? door=Msx1IntroPresentation.DoorSprite(frame.Pose);
                if(state is >=5 and <=7 && frame.Pose>=0)
                {
                    Assert.Equal(machine.Memory(0xEDD8)==6?"vick_d_01":"vick_d_00",door);
                    Assert.Equal(machine.Memory(0xEDCC),Msx1IntroPresentation.DoorX(frame.Pose,frame.X));
                }
                else Assert.Null(door);
                var attributes=machine.Vram.AsSpan(sat+16,16).ToArray();
                var native=Tms9918.DecodeSpriteRegion(machine.Vram,machine.VdpRegisters,attributes,0,0,256,192);
                var replay=new byte[256*192];
                if(frame.Pose>=0)for(int y=0;y<32 && frame.Y+y<192;y++)for(int x=0;x<16 && frame.X+x<256;x++)
                    replay[(frame.Y+y)*256+frame.X+x]=strip.Indices[y*strip.Width+frame.Pose*16+x];
                Assert.True(native.AsSpan().SequenceEqual(replay),"The recorded actor differs from its native vblank sprite layer.");
                Assert.Equal(machine.Memory(0xE257)>=10?1:0,frame.Background);
            }
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
