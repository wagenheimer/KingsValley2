using System.Text.Json;
using KingsValley.Engine.Extraction;
using KingsValley.Engine.Scenes;
using Microsoft.Xna.Framework;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class Msx1PatternExtractionTests
{
    [LocalRomFact("msx1")]
    public void ExtractedColorFramesAndPlaybackMatchNativeRoutinesInAllSixPyramids()
    {
        byte[] rom=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms","kv2_msx1.rom"));
        string root=Path.Combine(Path.GetTempPath(),"kv-patterns-"+Guid.NewGuid().ToString("N"));
        try
        {
            var data=Msx1PatternAnimations.Create(rom);
            byte[] fixtureVram=new byte[16384],fixtureRegisters=new byte[8];
            fixtureRegisters[2]=14;fixtureRegisters[3]=255;fixtureRegisters[4]=3;
            fixtureVram[0x3800]=0x98;fixtureVram[0x4C0]=128;fixtureVram[0x24C0]=0xA1;
            data.CaptureRoom(fixtureVram,fixtureRegisters,1,99);data.Save(root);
            using(var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Rooms","animations.json"))))
            {
                var cell=Assert.Single(fixture.RootElement.GetProperty("rooms").GetProperty("s01_r99").EnumerateArray());
                Assert.Equal("torch",cell.GetProperty("type").GetString());
                Assert.Equal(128,cell.GetProperty("pat")[0].GetInt32());
                Assert.Equal(0xA1,cell.GetProperty("col")[0].GetInt32());
            }
            var machine=RomScreens.BootToGameplay(rom);
            for(int pyramid=1;pyramid<=6;pyramid++)
            {
                int stage=1+(pyramid-1)*10;
                machine.Write(0xE241,(byte)pyramid); machine.Write(0xE242,(byte)stage);
                machine.CallAddress(RomStageState.ResolveStageLoader(rom),2_000_000);
                machine.Write(0xE243,1);machine.CallAddress(0x5AEC,2_000_000);
                data.CaptureRoom(machine.Vram,machine.VdpRegisters,stage,1); data.Save(root);
                Msx1PatternAnimations.ValidateFile(Path.Combine(root,"Rooms","animations.json"),new[]{$"s{stage:00}_r01"});
                using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Rooms","animations.json")));
                var eye=json.RootElement.GetProperty("pyramids").GetProperty(pyramid.ToString());
                int period=eye.GetProperty("period").GetInt32(),first=eye.GetProperty("base_pattern").GetInt32();
                Assert.Equal(new[]{12,18,16,5,12,4}[pyramid-1],period);
                for(int frame=3;frame>=0;frame--)
                {
                    machine.Write(0xEF00,(byte)(period-1));machine.Write(0xEF01,1);machine.Write(0xEF02,(byte)(frame+1));
                    machine.CallAddress(0x58C5,100_000);
                    byte[] expected=Rows(eye.GetProperty("frames")[frame]);
                    for(int third=0;third<3;third++) Assert.Equal(expected,machine.Vram.AsSpan(third*2048+first*8,expected.Length).ToArray());
                }
                var torch=json.RootElement.GetProperty("torch");
                for(int frame=7;frame>=0;frame--)
                {
                    machine.Write(0xEF05,3);machine.Write(0xEF06,(byte)((frame+7)&7));
                    machine.CallAddress(0x5915,100_000);
                    byte[] expected=Rows(torch.GetProperty("frames")[frame]);
                    for(int third=0;third<3;third++) Assert.Equal(expected,machine.Vram.AsSpan(third*2048+0x4C0,expected.Length).ToArray());
                }
                for(ushort address=0xEF00;address<=0xEF06;address++) machine.Write(address,0);
                var animator=new Msx1PatternAnimator(); animator.EnsureLoaded(Path.Combine(root,"Rooms"));
                string key=$"s{stage:00}_r01";
                var cells=json.RootElement.GetProperty("rooms").GetProperty(key).EnumerateArray().ToArray();
                Assert.NotEmpty(cells);
                for(int tick=1;tick<=period*6;tick++)
                {
                    machine.CallAddress(0x58C5,100_000);animator.Update(1f/30f,pyramid);
                    byte[] native=Tms9918.DecodeScreen2(machine.Vram,machine.VdpRegisters);
                    var pixels=new Color[256*192];Assert.True(animator.Apply(key,pyramid,pixels));
                    foreach(var cell in cells)
                    {
                        int x=cell.GetProperty("x").GetInt32()*8,y=cell.GetProperty("y").GetInt32()*8;
                        for(int row=0;row<8;row++) for(int col=0;col<8;col++)
                        {
                            int index=(y+row)*256+x+col;var c=Tms9918.Palette[native[index]];
                            Assert.Equal(new Color(c.R,c.G,c.B),pixels[index]);
                        }
                    }
                    // The real clean rooms may not reference the light-wave codes. A separate
                    // synthetic cell verifies their playback clock against the live native color bank.
                    var lightPixels=new Color[256*192];Assert.True(animator.Apply("s01_r99",pyramid,lightPixels));
                    for(int y=0;y<8;y++) for(int x=0;x<8;x++)
                    {
                        byte colors=machine.Vram[0x4C0+y];
                        int color=y==0 && x==0?colors>>4:colors&15;
                        var c=Tms9918.Palette[color];Assert.Equal(new Color(c.R,c.G,c.B),lightPixels[y*256+x]);
                    }
                }
            }
            Assert.Throws<InvalidDataException>(()=>Msx1PatternAnimations.ValidateFile(Path.Combine(root,"Rooms","animations.json"),new[]{"s60_r99"}));
            var damaged=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root,"Rooms","animations.json")))!;
            damaged["torch"]!["frames"]!.AsArray().RemoveAt(0);
            File.WriteAllText(Path.Combine(root,"Rooms","animations.json"),damaged.ToJsonString());
            Assert.Throws<InvalidDataException>(()=>Msx1PatternAnimations.ValidateFile(Path.Combine(root,"Rooms","animations.json"),Array.Empty<string>()));
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
        static byte[] Rows(JsonElement frame)=>frame.EnumerateArray().SelectMany(p=>p.EnumerateArray().Select(v=>v.GetByte())).ToArray();
    }
}
