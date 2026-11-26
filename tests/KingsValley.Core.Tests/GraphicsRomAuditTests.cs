using System.Security.Cryptography;
using System.Text.Json;
using KingsValley.Core.Screens;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Core.Tests;

public class GraphicsRomAuditTests
{
    private static byte[] Rom(string variant) => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"KingsValley","roms",variant=="msx1"?"kv2_msx1.rom":"kv2_msx2_scc.rom"));

    [LocalRomFact("msx1-frs")]
    public void FrsRevisionIsIdentifiedByBytesAndItsMainLoopIntroCompletes()
    {
        string path=Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_FRS_ROM")!;
        byte[] rom=File.ReadAllBytes(path);
        Assert.Equal(RomFolderManager.Msx1FrsSha1,Convert.ToHexString(SHA1.HashData(rom)));
        string root=Path.Combine(Path.GetTempPath(),"kv-frs-intro-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string renamed=Path.Combine(root,"unlabelled.bin");File.WriteAllBytes(renamed,rom);
            var info=RomFolderManager.InspectRomFile(renamed);
            Assert.NotNull(info);Assert.True(info.IsSupported);Assert.Equal(ScannedRomFlavor.Msx1,info.Flavor);
            Msx1IntroAnimation.Write(rom,root);
            var animation=Wagenheimer.MsxGraphics.Video.NativeActorAnimation.Load(Path.Combine(root,"Screens","intro_vick.json"));
            Assert.Equal(824,animation.Frames.Count);Assert.Equal(5,animation.PoseCount);
            Assert.Contains(animation.Frames,f=>f.Pose>=0);
            Assert.Contains(animation.Frames,f=>f.Background==1);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [LocalRomFact("msx1")]
    public void CancellationRetainsTheExistingPointerAndRejectsWrongCartridgeIdentity()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-cancel-"+Guid.NewGuid().ToString("N"));
        try
        {
            byte[] rom=Rom("msx1");
            Assert.Throws<InvalidDataException>(()=>GraphicsExtractionContext.Create(rom,"msx2",root));
            Assert.False(Directory.Exists(root));
            var context=GraphicsExtractionContext.Create(rom,"msx1",root,1);
            rom[0]^=1;
            Assert.Equal(context.RomSha1,Convert.ToHexString(SHA1.HashData(context.Rom)).ToLowerInvariant());
            Directory.CreateDirectory(context.Root);
            string pointer=Path.Combine(context.Root,"current.json");
            const string previous="previous generation pointer must remain untouched";
            File.WriteAllText(pointer,previous);
            var result=GraphicsExtractionCoordinator.Extract(context,token:new CancellationToken(true));
            Assert.Equal("cancelled",result.Manifest.Outcome); Assert.False(result.Manifest.Ready);
            Assert.Equal(previous,File.ReadAllText(pointer));
            Assert.True(File.Exists(Path.Combine(result.Directory,GraphicsExtractionManifest.FileName)));
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }

    [LocalRomFact("msx1")]
    public void NativeRoomRedrawRemovesCollectiblesAndPreservesTmsPalette()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-clean-room-"+Guid.NewGuid().ToString("N"));
        try
        {
            byte[] rom=Rom("msx1"); string levels=Path.Combine(root,"Levels");
            RomStageJson.WriteAll(rom,levels,maxStages:1);
            RomRoomDumps.WriteAll(rom,levels,root,stages:1);
            string native=Path.Combine(root,"Art","Native");
            string[] sources=Directory.GetFiles(native,"*.source.json").Where(p=>Path.GetFileName(p).StartsWith("item_") || Path.GetFileName(p).StartsWith("soulstone_")).ToArray();
            Assert.NotEmpty(sources);
            foreach(string source in sources)
            {
                using var metadata=JsonDocument.Parse(File.ReadAllText(source)); var m=metadata.RootElement;
                int stage=m.GetProperty("stage").GetInt32(),room=m.GetProperty("room").GetInt32(),x=m.GetProperty("x").GetInt32(),y=m.GetProperty("y").GetInt32();
                var original=ScreenAssembler.DecodeFile(source[..^".source.json".Length]);
                var clean=ScreenAssembler.DecodeFile(Path.Combine(root,"Rooms","dumps",$"s{stage:00}_r{room:00}.bin"));
                int changed=0;
                for(int row=0;row<original.Height;row++) for(int col=0;col<original.Width;col++)
                    if(original.Indices[row*original.Width+col]!=clean.Indices[(y+row)*256+x+col]) changed++;
                Assert.True(changed>0,"The collectible remained baked into its background: "+Path.GetFileName(source));
                Assert.Equal(new ScreenColor(33,200,66),clean.Palette[2]);
            }
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }

    [LocalRomFact("msx2")]
    public void Msx2BlocksMatchTheBaselineAndTheAtlasHasNoBlankOrDuplicateNames()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-msx2-audit-"+Guid.NewGuid().ToString("N"));
        try
        {
            var context=GraphicsExtractionContext.Create(Rom("msx2"),"msx2",root,1);
            var result=GraphicsExtractionCoordinator.Extract(context);
            Assert.False(result.Manifest.Ready);
            Assert.DoesNotContain(result.Manifest.Checks,c=>c.State is "unsupported" or "failed");
            Assert.All(result.Manifest.Checks.Where(c=>c.Component.StartsWith("screen-",StringComparison.Ordinal)),c=>Assert.Equal("valid",c.State));
            string[] baseline={"e1c0857447480c3431e606bf79d0db2acccd4df878115ac54f8a28f12f0c8e0d","6fbef7c74b9c6b3147888af18de213327c7ef0440055ccf0ebcd78f2f1527688","d46668dfecc3e1023a078c92efb9d31913fa7f9c1b34823ba02897f27c98e7ab","71d3d9b6e7d087427a4364733a717fbb4b6d0194658cb1efc34963537b49a56c","a127ac41f81d5210b1d4e3118c70acacfe2b4dbedc94514a71376dc6bd443625","fb29aacf971db668099194b8a5db75072db0aaf73579715d5beca63fe2c014b2"};
            for(int p=1;p<=6;p++) Assert.Equal(baseline[p-1],Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(result.Directory,"Rooms",$"blocks_p{p}.bin")))).ToLowerInvariant());
            string art=Path.Combine(result.Directory,"Art","kv2.atlas");
            var texture=PngRgba.Decode(File.ReadAllBytes(Path.ChangeExtension(art,".png")));
            var regions=AtlasInventory.Read(File.ReadAllText(art),texture);
            Assert.All(regions,r=>Assert.True(r.VisiblePixels>0,r.Name));
            var glyph=regions.Single(r=>r.Name=="font_A");
            for(int y=0;y<glyph.Height;y++)for(int x=0;x<glyph.Width;x++)
            {
                int offset=((glyph.Y+y)*texture.Width+glyph.X+x)*4;
                if(texture.Pixels[offset+3]!=0)Assert.Equal(new byte[]{255,255,255},texture.Pixels.AsSpan(offset,3).ToArray());
            }
            var hud=regions.Single(r=>r.Name=="hud_strip");
            var letterH=regions.Single(r=>r.Name=="font_H");
            for(int y=0;y<8;y++)for(int x=0;x<8;x++)
            {
                int letter=((letterH.Y+y)*texture.Width+letterH.X+x)*4;
                int target=((hud.Y+2+y)*texture.Width+hud.X+16+x)*4;
                Assert.Equal(texture.Pixels[letter+3]!=0,texture.Pixels[target]==255 && texture.Pixels[target+1]==255 && texture.Pixels[target+2]==255);
            }
            Assert.True(Enumerable.Range(0,hud.Width*hud.Height).Any(i=>
            {
                int offset=((hud.Y+i/hud.Width)*texture.Width+hud.X+i%hud.Width)*4;
                return texture.Pixels[offset]==255 && texture.Pixels[offset+1]==255 && texture.Pixels[offset+2]==255;
            }),"Native HUD labels must remain visible, not just the dynamic number fields.");
            var strip=ScreenAssembler.DecodeFile(Path.Combine(result.Directory,"Screens","intro_vick.bin"));
            string[] poses={"vick_s0_03","vick_s0_04","vick_s0_05","vick_d_00","vick_d_01"};
            for(int pose=0;pose<poses.Length;pose++)
            {
                var region=regions.Single(r=>r.Name==poses[pose]);
                for(int y=0;y<32;y++) for(int x=0;x<16;x++)
                {
                    int source=((region.Y+y)*texture.Width+region.X+x)*4;
                    var pixel=strip.Palette[strip.Indices[y*80+pose*16+x]];
                    Assert.Equal(texture.Pixels[source+3],pixel.A);
                    if(pixel.A!=0) { Assert.Equal(texture.Pixels[source],pixel.R); Assert.Equal(texture.Pixels[source+1],pixel.G); Assert.Equal(texture.Pixels[source+2],pixel.B); }
                }
            }
            foreach(string screen in new[]{"konami","title","start_select","world_map"}) Assert.Contains(ScreenAssembler.DecodeFile(Path.Combine(result.Directory,"Screens",screen+".bin")).Indices,p=>p>=2);
            Assert.Null(GraphicsGenerationStore.Current(context.Root,"msx2"));
        }
        finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
}
