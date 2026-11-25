using KingsValley.Engine.Extraction;
using KingsValley.Engine.Graphics;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class Msx1PlayerAnimationTests
{
    [LocalRomFact("msx1")]
    public void HeldUseAndDoorPosesMatchTheNativePatternLoaderAndSatWriters()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx1.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-player-poses-"+Guid.NewGuid().ToString("N"));
        try
        {
            RomSpriteAtlas.Build(rom,root);
            var texture=PngRgba.Decode(File.ReadAllBytes(Path.Combine(root,"Art","kv2.png")));
            var regions=AtlasInventory.Read(File.ReadAllText(Path.Combine(root,"Art","kv2.atlas")),texture);
            var machine=RomScreens.BootToGameplay(rom);
            machine.Write(0xE24C,0);machine.Write(0xE298,0);machine.Write(0xE280,0);
            machine.Write(0xE282,80);machine.Write(0xE284,80);
            for(int set=1;set<=6;set++)for(int pose=10;pose<14;pose++)
            {
                machine.Write(0xE287,(byte)set);machine.Write(0xE285,(byte)pose);machine.Write(0xE294,(byte)(pose>=12?1:0));
                machine.CallAddress(0x4192);machine.CallAddress(0x9CA3);machine.CallAddress(0x565F,500_000);
                Compare($"vick_s{set}_{pose:00}",pose>=12?82:80);
            }
            machine.CallAddress(0x531F,500_000); // Native exit RLE B61D, eight consecutive SAT poses.
            for(int pose=0;pose<8;pose++)
            {
                machine.Write(0xE282,80);machine.Write(0xE258,(byte)(pose/2));machine.Write(0xE259,(byte)(pose%2==1?0x12:2));
                machine.CallAddress(0x8CE4,100_000);
                Compare($"vick_d_{pose:00}",80);
                if(pose<2)
                {
                    var region=Assert.Single(regions,r=>r.Name==$"vick_d_{pose:00}");
                    var opaqueRows=Enumerable.Range(0,32).Where(y=>Enumerable.Range(0,16)
                        .Any(x=>texture.Pixels[((region.Y+y)*texture.Width+region.X+x)*4+3]!=0)).ToArray();
                    Assert.Equal(Msx1IntroPresentation.DoorTopPadding,opaqueRows[0]);
                    Assert.Equal(24,opaqueRows[^1]);
                }
            }
            void Compare(string name,int x)
            {
                byte[] attributes=Enumerable.Range(0,16).Select(i=>machine.Memory((ushort)(0xE800+i))).ToArray();
                byte[] native=Tms9918.DecodeSpriteRegion(machine.Vram,machine.VdpRegisters,attributes,x,72,16,32);
                var region=Assert.Single(regions,r=>r.Name==name);
                for(int i=0;i<native.Length;i++)
                {
                    int offset=((region.Y+i/16)*texture.Width+region.X+i%16)*4;
                    var color=Tms9918.Palette[native[i]];
                    Assert.Equal(native[i]==0?0:255,texture.Pixels[offset+3]);
                    if(native[i]!=0)Assert.Equal(new[]{color.R,color.G,color.B},texture.Pixels.AsSpan(offset,3).ToArray());
                }
            }
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
