using KingsValley.Engine.Extraction;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class Msx1ObjectStateTests
{
    [LocalRomFact("msx1")]
    public void AtlasCoffinsMatchNativeMummySpawnOpenAndCloseInAllPyramids()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx1.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-object-states-"+Guid.NewGuid().ToString("N"));
        try
        {
            RomSpriteAtlas.Build(rom,root);
            var texture=PngRgba.Decode(File.ReadAllBytes(Path.Combine(root,"Art","kv2.png")));
            var regions=AtlasInventory.Read(File.ReadAllText(Path.Combine(root,"Art","kv2.atlas")),texture);
            var machine=RomScreens.BootToGameplay(rom);
            for(int pyramid=1;pyramid<=6;pyramid++)
            {
                machine.Write(0xE241,(byte)pyramid);machine.Write(0xE242,(byte)(1+(pyramid-1)*10));
                RomStageState.LoadMsx1Theme(machine,rom);
                machine.CallAddress(RomStageState.ResolveStageLoader(rom),2_000_000);
                machine.Write(0xE243,1);
                for(int address=0xE300;address<0xE780;address++) machine.Write((ushort)address,0);
                machine.CallAddress(0x4192,100_000);
                machine.CallRoutine(0xA9E8,a:1,de:0x4048,bc:1); // Actor Y=72; native coffin top is Y-8=64.
                Compare($"coffin_ajar_p{pyramid}");
                machine.Write(0xE514,1);machine.CallAddress(0x6356,200_000);
                Compare($"coffin_open_p{pyramid}");
                machine.Write(0xE514,1);machine.CallAddress(0x6356,200_000);
                Compare($"coffin_p{pyramid}");
                for(int code=6;code<24;code++)
                {
                    machine.CallRoutine(0x53EF,a:(byte)code,de:0x4040);
                    Compare($"crumble_{code}_p{pyramid}");
                }
                for(int shape=0;shape<3;shape++) for(int phase=0;phase<3;phase++)
                {
                    // Native floor/wall tool handlers: IX points to the first E300 record.
                    machine.CallRoutine(0x8F9D,a:1);
                    machine.Write(0xE305,(byte)(phase-1));machine.Write(0xE306,1);
                    machine.Write(0xE2A2,64);machine.Write(0xE2A3,64);
                    machine.Write(0xE294,(byte)(shape==2?1:0));
                    machine.CallAddress((ushort)(shape==0?0xA629:0xA706),200_000);
                    int first=(shape==0?6:shape==1?12:18)+phase*2;
                    Compare($"crumble_{first}_p{pyramid}");
                    Compare($"crumble_{first+1}_p{pyramid}",shape==0?72:64,shape==0?64:72);
                }
            }
            void Compare(string name,int sourceX=64,int sourceY=64)
            {
                var region=Assert.Single(regions,r=>r.Name==name);
                byte[] native=Tms9918.DecodeScreen2(machine.Vram,machine.VdpRegisters);
                for(int y=0;y<region.Height;y++) for(int x=0;x<region.Width;x++)
                {
                    var c=Tms9918.Palette[native[(sourceY+y)*256+sourceX+x]];
                    int offset=((region.Y+y)*texture.Width+region.X+x)*4;
                    Assert.Equal(c.R,texture.Pixels[offset]);Assert.Equal(c.G,texture.Pixels[offset+1]);
                    Assert.Equal(c.B,texture.Pixels[offset+2]);Assert.Equal(255,texture.Pixels[offset+3]);
                }
            }
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
}
