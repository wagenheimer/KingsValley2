using KingsValley.Core.Gameplay;
using KingsValley.Core.Screens;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class Msx1DoorTerrainTests
{
    [LocalRomFact("msx1")]
    public void NativeDoorsStudsMovableBlocksAndTerrainMatchAtlasInAllPyramids()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx1.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-door-terrain-"+Guid.NewGuid().ToString("N"));
        try
        {
            RomSpriteAtlas.Build(rom,root);
            var image=PngRgba.Decode(File.ReadAllBytes(Path.Combine(root,"Art","kv2.png")));
            var regions=AtlasInventory.Read(File.ReadAllText(Path.Combine(root,"Art","kv2.atlas")),image).ToDictionary(r=>r.Name);
            byte[] Pixels(string name)
            {
                var r=regions[name];var result=new byte[r.Width*r.Height*4];
                for(int y=0;y<r.Height;y++) image.Pixels.AsSpan(((r.Y+y)*image.Width+r.X)*4,r.Width*4).CopyTo(result.AsSpan(y*r.Width*4));
                return result;
            }
            for(int total=1;total<=16;total++)
            {
                int offset=13*8192+0xB9AD-0xA000+total*2; // Native 4D4D includes the unused index-zero row.
                int pointer=rom[offset]|rom[offset+1]<<8;
                Assert.Equal(rom.AsSpan(13*8192+pointer-0xA000,total).ToArray(),DoorStudLayouts.For(total));
            }
            var clean=ScreenAssembler.DecodeFile(Path.Combine(root,"Rooms","door_clean.bin"));
            var machine=RomScreens.BootToGameplay(rom);
            for(int pyramid=1;pyramid<=6;pyramid++)
            {
                machine.Write(0xE241,(byte)pyramid);machine.Write(0xE242,(byte)(1+(pyramid-1)*10));
                machine.CallAddress(RomStageState.ResolveStageLoader(rom),2_000_000);machine.Write(0xE243,1);
                machine.CallAddress(0x4192);
                machine.Write(0xE2F1,64);machine.Write(0xE2F2,64);machine.Write(0xE2F3,1);machine.Write(0xE2F5,0);
                for(int frame=0;frame<3;frame++)
                {
                    machine.Write(0xE2F6,(byte)frame);machine.CallAddress(0x8C14,100_000);
                    Compare(Pixels($"door_f{frame}_p{pyramid}"),32,32);
                }
                byte[] closed=Pixels($"door_f0_p{pyramid}");
                for(int i=0;i<clean.Indices.Length;i++)
                {
                    var c=clean.Palette[clean.Indices[i]];
                    Assert.Equal(new[]{c.R,c.G,c.B,(byte)255},closed.AsSpan(i*4,4).ToArray());
                }
                for(int remaining=0;remaining<=8;remaining++)
                {
                    machine.Write(0xE2F6,0);machine.Write(0xE2F4,8);machine.Write(0xE2F5,(byte)remaining);
                    machine.CallAddress(0x8C14,100_000);
                    byte[] expected=(byte[])closed.Clone(),stud=Pixels($"door_stud_p{pyramid}");
                    foreach(int slot in DoorStudLayouts.For(8).Take(remaining))
                        for(int y=0;y<8;y++) stud.AsSpan(y*8*4,8*4).CopyTo(expected.AsSpan(((slot/4*8+y)*32+slot%4*8)*4));
                    Compare(expected,32,32);
                }
                for(int a=0xE600;a<0xE700;a++) machine.Write((ushort)a,0);
                machine.Write(0xE600,5);machine.Write(0xE602,64);machine.Write(0xE603,64);machine.Write(0xE604,1);
                machine.CallAddress(0x626B,100_000);Compare(Pixels($"movable_block_p{pyramid}"),16,16);
                foreach(var terrain in new[]{(Name:"soft",Map:(byte)0xAA),(Name:"wall",Map:(byte)0xFF)})
                {
                    for(int a=0xE900;a<0xE9C0;a++) machine.Write((ushort)a,terrain.Map);
                    machine.Write(0xE200,11); // Native editor mode suppresses separate dynamic ladder records.
                    machine.CallAddress(0x41B8);machine.CallAddress(0x453D,1_000_000);machine.CallAddress(0x4192);
                    for(int variant=0;variant<4;variant++) Compare(Pixels($"{terrain.Name}_p{pyramid}_{variant}"),8,8);
                }
                for(int a=0xE900;a<0xE9C0;a++) machine.Write((ushort)a,0x55);
                machine.CallAddress(0x41B8);machine.CallAddress(0x453D,1_000_000);machine.CallAddress(0x4192);
                int names=(machine.VdpRegisters[2]&15)<<10;
                var ladderCodes=new HashSet<byte>();
                for(int row=8;row<12;row++) for(int col=8;col<12;col++)
                {
                    byte code=machine.Vram[names+row*32+col];Assert.True(code is 3 or 4);ladderCodes.Add(code);
                    Compare(Pixels($"{(code==3?"ladder":"ladder_alt")}_p{pyramid}"),8,8,col*8,row*8);
                }
                Assert.Equal(2,ladderCodes.Count);
            }
            void Compare(byte[] expected,int width,int height,int sourceX=64,int sourceY=64)
            {
                byte[] native=Tms9918.DecodeScreen2(machine.Vram,machine.VdpRegisters);
                for(int y=0;y<height;y++) for(int x=0;x<width;x++)
                {
                    var c=Tms9918.Palette[native[(sourceY+y)*256+sourceX+x]];int o=(y*width+x)*4;
                    Assert.Equal(c.R,expected[o]);Assert.Equal(c.G,expected[o+1]);Assert.Equal(c.B,expected[o+2]);Assert.Equal(255,expected[o+3]);
                }
            }
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
}
