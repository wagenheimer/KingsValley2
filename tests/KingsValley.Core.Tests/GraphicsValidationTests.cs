using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxGraphics.Formats;
using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class GraphicsValidationTests
{
    [Fact]
    public void BoundedRleDiscoversOutputLengthWithoutAcceptingOverflowOrAddressCommands()
    {
        Assert.Equal(new byte[]{7,7,3,4},KonamiRle.DecodeBounded(new byte[]{2,7,130,3,4,0},0,8));
        Assert.Throws<InvalidDataException>(()=>KonamiRle.DecodeBounded(new byte[]{9,7,0},0,8));
        Assert.Throws<InvalidDataException>(()=>KonamiRle.DecodeBounded(new byte[]{128,1,2,0},0,8));
        Assert.Throws<InvalidDataException>(()=>KonamiRle.DecodeBounded(new byte[]{2,7},0,8));
    }
    [Fact]
    public void IsolatedSpriteRegionUsesNativeRegistersAndDoesNotMutateTheSnapshot()
    {
        byte[] vram=new byte[16384],regs=new byte[8];
        regs[1]=3; regs[5]=0x20; regs[6]=3; // Magnified 16x16 sprites.
        vram[0x1800]=128;
        // A pre-existing opaque sprite must not leak into the isolated region.
        vram[0x1000]=255; vram[0x1003]=15; vram[0x1004]=208;
        byte[] original=(byte[])vram.Clone(), originalRegs=(byte[])regs.Clone();
        byte[] attributes={255,32,3,0x86}; // Early clock; pattern index rounds down to 0.
        byte[] pixels=Tms9918.DecodeSpriteRegion(vram,regs,attributes,0,0,4,3);
        Assert.Equal(new byte[]{6,6,0,0,6,6,0,0,0,0,0,0},pixels);
        Assert.Equal(original,vram); Assert.Equal(originalRegs,regs);
        Assert.Throws<ArgumentException>(()=>Tms9918.DecodeSpriteRegion(vram,regs,new byte[3],0,0,4,3));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Tms9918.DecodeSpriteRegion(vram,regs,attributes,255,0,4,3));
    }

    [Fact]
    public void NativePaletteIndexingPreservesBlackAlphaAndRejectsColorApproximation()
    {
        byte[] rgba={0,0,0,255,255,255,255,0,33,200,66,255};
        Assert.Equal(new byte[]{1,0,2},PaletteIndexing.FromRgba(rgba,Tms9918.Palette,transparentZero:true));
        Assert.Throws<InvalidDataException>(()=>PaletteIndexing.FromRgba(new byte[]{34,200,66,255},Tms9918.Palette,true));
        Assert.Throws<InvalidDataException>(()=>PaletteIndexing.FromRgba(new byte[]{33,200,66,128},Tms9918.Palette,true));
    }
    [Fact]
    public void CorruptedManifestCollectionsAreNotConsideredAnAvailableGeneration()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-corrupt-manifest-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach(string json in new[]{"{\"Checks\":null}","{\"Artifacts\":[null]}","not json"})
            {
                File.WriteAllText(Path.Combine(root,GraphicsExtractionManifest.FileName),json);
                Assert.Null(GraphicsExtractionManifest.Read(root));
            }
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public void RawScreenIndicesAndResolvedBackdropAreExplicitlyDifferentViews()
    {
        byte[] vram=new byte[16384],regs=new byte[8]; regs[7]=6;
        Assert.Equal(0,Tms9918.DecodeScreen2(vram,regs)[0]);
        Assert.Equal(6,Tms9918.DecodeScreen2(vram,regs,resolveBackdrop:true)[0]);
    }
    [Fact]
    public void PngReaderDistinguishesOpaqueBlackFromTransparencyAndChecksIntegrity()
    {
        // Independent 2x2 RGBA fixture encoded by Pillow, including a filtered second scanline.
        byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGElEQVR4nGNgYGD4////fwYGxRNO/0EcAD/mByaSRhVNAAAAAElFTkSuQmCC");
        var image=PngRgba.Decode(png);
        Assert.Equal(2,image.Width); Assert.Equal(2,image.Height);
        Assert.Equal(1,image.VisiblePixels(0,0,1,1)); Assert.Equal(0,image.VisiblePixels(1,0,1,1));
        Assert.Equal(new byte[]{33,200,66,255},image.Pixels.AsSpan(8,4).ToArray());
        png[45]^=1;
        Assert.Throws<InvalidDataException>(()=>PngRgba.Decode(png));
    }
    [Fact]
    public void TmsPaletteDoesNotRoundThroughV9938ThreeBitChannels()
    {
        var palette=Tms9918.Palette.Select(c=>new KingsValley.Core.Screens.ScreenColor(c.R,c.G,c.B)).ToArray();
        byte[] encoded=KingsValley.Core.Screens.ScreenAssembler.Encode(2,1,palette,new byte[]{2,7},paletteModel:KingsValley.Core.Screens.ScreenPaletteModel.Tms9918);
        var decoded=KingsValley.Core.Screens.ScreenAssembler.Decode(encoded);
        Assert.Equal(palette[2],decoded.Palette[2]); Assert.Equal(palette[7],decoded.Palette[7]);
        Assert.Equal(39,encoded.Length);
    }

    [Fact]
    public void RleRejectsShortOverlongAndUnterminatedSprites()
    {
        Assert.Equal(new byte[]{7,7},KonamiRle.Decode(new byte[]{2,7,0},0,2));
        Assert.Throws<InvalidDataException>(()=>KonamiRle.Decode(new byte[]{1,7,0},0,2));
        Assert.Throws<InvalidDataException>(()=>KonamiRle.Decode(new byte[]{3,7,0},0,2));
        Assert.Throws<InvalidDataException>(()=>KonamiRle.Decode(new byte[]{2,7},0,2));
    }

    [Fact]
    public void IncompleteAttemptCannotBecomeCurrentAndUnknownPointerCannotEscapeRoot()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv-generation-"+Guid.NewGuid().ToString("N"));
        try
        {
            string staging=GraphicsGenerationStore.Begin(root);
            File.WriteAllText(Path.Combine(staging,"evidence.txt"),"partial");
            string result=GraphicsGenerationStore.Finish(root,staging,new GraphicsExtractionManifest {Variant="msx1"});
            Assert.True(File.Exists(Path.Combine(result,"evidence.txt")));
            Assert.Null(GraphicsGenerationStore.Current(root,"msx1"));
            File.WriteAllText(Path.Combine(root,"current.json"),"{\"Generation\":\"../../opposite\"}");
            Assert.Null(GraphicsGenerationStore.Current(root,"msx1"));
            Assert.Throws<InvalidOperationException>(()=>GraphicsGenerationStore.Finish(root,root,new()));
        }
        finally {Directory.Delete(root,true);}
    }
    [Fact]
    public void Screen2UsesColorAddressMaskIncludingLowerCharacterBits()
    {
        byte[] vram = new byte[16384], regs = new byte[8];
        regs[2] = 6; regs[3] = 0x81; regs[4] = 4;
        vram[0x1800] = 24;
        // R3's low mask mirrors code 24 to code 8, for both pattern and color addressing.
        vram[0x2000 + 64] = 0x80; vram[0x2000 + 192] = 0;
        // Pattern/color tables intentionally overlap for this address-bus fixture.
        vram[0x2000 + 64] = 0xA1;
        byte[] pixels = Tms9918.DecodeScreen2(vram, regs);
        Assert.Equal(10, pixels[0]); Assert.Equal(1, pixels[1]);
    }

    [Fact]
    public void SpritesHonorEarlyClockPriorityAndQuadrantOrder()
    {
        byte[] vram = new byte[16384], regs = new byte[8];
        regs[1] = 2; regs[5] = 0x20; regs[6] = 3;
        vram[0x1000] = 255; vram[0x1001] = 32; vram[0x1003] = 0x8F;
        vram[0x1004] = 255; vram[0x1005] = 0; vram[0x1007] = 6; vram[0x1008] = 208;
        vram[0x1800] = 128; vram[0x1808] = 64; vram[0x1810] = 32;
        byte[] pixels = Tms9918.DecodeSprites(vram, regs);
        Assert.Equal(15, pixels[0]); Assert.Equal(15, pixels[8 * 256 + 1]); Assert.Equal(15, pixels[10]);
    }

    [Fact]
    public void AtlasRejectsDuplicatesAndReportsTransparentRegions()
    {
        var texture = new PngRgba(2, 1, new byte[] {0,0,0,0,0,0,0,255});
        var regions = AtlasInventory.Read("empty\n0,0,1,1\n0,0\nblack\n1,0,1,1\n0,0\n", texture);
        Assert.Equal(0, regions[0].VisiblePixels); Assert.Equal(1, regions[1].VisiblePixels);
        Assert.Throws<InvalidDataException>(() => AtlasInventory.Read("same\n0,0,1,1\n0,0\nsame\n1,0,1,1\n0,0", texture));
    }

    [Fact]
    public void MissingRomCannotProduceZeroFilledMsx2Blocks()
    {
        string root = Path.Combine(Path.GetTempPath(), "kv-blocks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { Assert.Throws<ArgumentNullException>(() => RomDirectExtractor.EnsureBlocks(root, null)); Assert.Empty(Directory.GetFiles(root)); }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CrossFlavorBaseCannotSatisfyAFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "kv-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"msx2"));
        File.WriteAllText(Path.Combine(root,"msx2","only.bin"), "opposite");
        try
        {
            var msx2 = new ArtSet("msx2", "MSX2", Path.Combine(root,"msx2"), ArtFlavor.Msx2);
            var msx1 = new ArtSet("msx1", "MSX1", Path.Combine(root,"msx1"), ArtFlavor.Msx1, "msx2");
            Assert.Equal(Path.Combine(msx1.Root,"only.bin"), ArtSets.Resolve(new[] {msx1,msx2}, msx1,"only.bin"));
        }
        finally { Directory.Delete(root,true); }
    }
}
