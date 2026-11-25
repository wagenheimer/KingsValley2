#nullable enable
using System;
using System.IO;
using System.Linq;
using KingsValley.Core.Screens;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

/// <summary>
/// Checks native MSX1 asset families and flavor isolation. A partial inventory must not enable a playable art set.
/// </summary>
/// <remarks>
/// The ROM is not versioned. Without it the ROM-driven tests are no-ops (same policy as
/// <see cref="RomScreensTests"/>). Set <c>KINGSVALLEY_TEST_ROM</c> to point at the image.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Msx1ExtractionTestCollection
{
    public const string Name = "MSX1 extraction (static asset state)";
}

[Collection(Msx1ExtractionTestCollection.Name)]
public class Msx1ExtractionTests
{
    private static string? FindRom()
    {
        string? configured = Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_ROM");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KingsValley", "roms", "kv2_msx1.rom"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "kv2-extract", "work", "rom", "kv2_msx1.rom"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KingsValley", "roms",
                "King's Valley 2 - The Seal Of El Giza - MSX1 Version - Konami (1988) [FRS patch] [RC-760] [2641].rom"),
            Path.Combine(Path.GetTempPath(), "opencode", "kv2rom", "kv2_msx1.rom"),
            Path.Combine(Path.GetTempPath(), "kv2rom", "kv2_msx1.rom"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "opencode", "kv2-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Test_DumpAuthenticMsx1DoorTiles()
    {
        string? romPath = FindRom();
        if (romPath is null) return;
        byte[] rom = File.ReadAllBytes(romPath);

        var machine = RomScreens.BootToGameplay(rom);
        byte[] vram = machine.Vram;
        byte[] regs = machine.VdpRegisters;

        int patBase = regs.Length > 4 ? (regs[4] & 0x3C) << 11 : 0x2000;
        int colBase = regs.Length > 3 ? (regs[3] & 0x80) << 6 : 0x0000;
        int pMask = regs.Length > 4 ? (regs[4] & 0x03) : 3;
        int cMask = regs.Length > 3 ? ((regs[3] & 0x7F) < 4 ? (regs[3] & 0x03) : 3) : 3;

        int[] msx1DoorCodes = { 174, 178, 198, 194, 175, 142, 144, 195, 176, 145, 116, 196, 177, 179, 199, 197 };
        int third = 0; // Top third where door is (y=16)
        int pBank = (third & pMask) * 0x800;
        int cBank = (third & cMask) * 0x800;

        byte[] door32x32 = new byte[32 * 32];
        for (int b = 0; b < 16; b++)
        {
            int code = msx1DoorCodes[b];
            int bx = (b & 3) * 8;
            int by = (b >> 2 & 3) * 8;
            for (int r = 0; r < 8; r++)
            {
                byte pat = vram[(patBase + pBank + code * 8 + r) & 0x3FFF];
                byte colB = vram[(colBase + cBank + code * 8 + r) & 0x3FFF];
                byte fg = (byte)((colB >> 4) & 0x0F);
                byte bg = (byte)(colB & 0x0F);
                for (int c = 0; c < 8; c++)
                {
                    bool bit = ((pat >> (7 - c)) & 1) != 0;
                    door32x32[(by + r) * 32 + (bx + c)] = bit ? fg : bg;
                }
            }
        }

        string outPath = @"C:\Users\Cezar Wagenheimer\.gemini\antigravity\brain\71d0b0d4-3b5f-4389-8cb4-8ca80e03f8e9\authentic_msx1_door32.bin";
        File.WriteAllBytes(outPath, door32x32);

        // Also decode stud code 96 (0x60)
        byte[] stud8x8 = new byte[64];
        for (int r = 0; r < 8; r++)
        {
            byte pat = vram[(patBase + pBank + 96 * 8 + r) & 0x3FFF];
            byte colB = vram[(colBase + cBank + 96 * 8 + r) & 0x3FFF];
            byte fg = (byte)((colB >> 4) & 0x0F);
            byte bg = (byte)(colB & 0x0F);
            for (int c = 0; c < 8; c++)
            {
                bool bit = ((pat >> (7 - c)) & 1) != 0;
                stud8x8[r * 8 + c] = bit ? fg : bg;
            }
        }
        string studPath = @"C:\Users\Cezar Wagenheimer\.gemini\antigravity\brain\71d0b0d4-3b5f-4389-8cb4-8ca80e03f8e9\authentic_msx1_stud8.bin";
        File.WriteAllBytes(studPath, stud8x8);
    }

    [Fact]
    public void Test_CheckFontA()
    {
        string? romPath = FindRom();
        if (romPath is null) return;
        byte[] rom = File.ReadAllBytes(romPath);
        var (vram, regs) = RomScreens.CaptureGameplay(rom);
        int patBase = (regs[4] & 0x3C) << 11;
        int middleBank = patBase + 0x800;
        int code = 0xE1;
        var bytes = new byte[8];
        for (int y = 0; y < 8; y++)
            bytes[y] = vram[(middleBank + code * 8 + y) & 0x3FFF];

        string log = $"Font_A VRAM bytes: {BitConverter.ToString(bytes)}\n";
        File.WriteAllText(@"C:\Users\Cezar Wagenheimer\.gemini\antigravity\brain\71d0b0d4-3b5f-4389-8cb4-8ca80e03f8e9\font_a_vram.txt", log);
    }

    [Fact]
    public void Test_GenerateMurals_FromLiveMachine()
    {
        string? romPath = FindRom();
        if (romPath is null) return;
        byte[] rom = File.ReadAllBytes(romPath);

        var machine = RomScreens.BootToGameplay(rom);
        byte[] vram = machine.Vram;
        byte[] regs = machine.VdpRegisters;

        int patBase = regs.Length > 4 ? (regs[4] & 0x3C) << 11 : 0x2000;
        int colBase = regs.Length > 3 ? (regs[3] & 0x80) << 6 : 0x0000;
        int pMask = regs.Length > 4 ? (regs[4] & 0x03) : 3;
        int cMask = regs.Length > 3 ? ((regs[3] & 0x7F) < 4 ? (regs[3] & 0x03) : 3) : 3;

        int third = 1;
        int pBank = (third & pMask) * 0x800;
        int cBank = (third & cMask) * 0x800;

        int off = 0x1C104; // Pyr 1
        byte[] pixels = new byte[64 * 32];
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                int code = rom[off + row * 8 + col];
                for (int r = 0; r < 8; r++)
                {
                    byte pat = vram[(patBase + pBank + code * 8 + r) & 0x3FFF];
                    byte colB = vram[(colBase + cBank + code * 8 + r) & 0x3FFF];
                    byte fg = (byte)((colB >> 4) & 0x0F);
                    byte bg = (byte)(colB & 0x0F);
                    for (int c = 0; c < 8; c++)
                    {
                        bool bit = ((pat >> (7 - c)) & 1) != 0;
                        pixels[(row * 8 + r) * 64 + (col * 8 + c)] = bit ? fg : bg;
                    }
                }
            }
        }

        int nonZero = pixels.Count(p => p != 0);
        Assert.True(nonZero > 500, $"Expected >500 lit mural pixels, got {nonZero}");

        // Save preview of pyramid 1 mural
        var tms = RomScreens.TmsPalette;
        byte[] packed = new byte[1024];
        for (int i = 0; i < 1024; i++)
            packed[i] = (byte)(((pixels[i * 2] & 0x0F) << 4) | (pixels[i * 2 + 1] & 0x0F));

    }

    [Fact]
    public void RoomDumps_AreWrittenInTheNativeScreenFormat()
    {
        string? romPath = FindRom();
        if (romPath is null)
            return;

        string root = NewTempDirectory();
        try
        {
            byte[] rom = File.ReadAllBytes(romPath);
            string levels = Path.Combine(root, "Levels");
            string extracted = Path.Combine(root, "extracted");
            Directory.CreateDirectory(levels);

            // Only stage 1 is needed here; the production call dumps all 60.
            File.WriteAllText(Path.Combine(levels, "stage01.json"), RomStageJson.Build(rom, 1));
            RomRoomDumps.WriteAll(rom, levels, extracted, stages: 1);

            string[] dumps = Directory.GetFiles(Path.Combine(extracted, "Rooms", "dumps"), "s01_r*.bin");
            Assert.NotEmpty(dumps);

            byte[] bytes = File.ReadAllBytes(dumps[0]);
            Assert.Equal(ScreenAssembler.HeaderSize + RomScreens.Width * RomScreens.Height / 2, bytes.Length);

            ScreenData screen = ScreenAssembler.Decode(bytes);
            Assert.Equal(RomScreens.Width, screen.Width);
            Assert.Equal(RomScreens.Height, screen.Height);
            Assert.Contains(screen.Indices, index => index >= 2); // a real room, not a blank capture


        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SpriteAtlasContainsOnlyNativeFramesAndLeavesUnsupportedFamiliesMissing()
    {
        string? romPath = FindRom();
        if (romPath is null)
            return;

        string root = NewTempDirectory();
        try
        {
            string extracted = Path.Combine(root, "extracted");
            RomSpriteAtlas.Build(File.ReadAllBytes(romPath), extracted,
                force: true, poseFrames: 20 * 60);

            string atlasPath = Path.Combine(extracted, "Art", "kv2.atlas");
            string pngPath = Path.Combine(extracted, "Art", "kv2.png");
            Assert.True(File.Exists(atlasPath));
            Assert.True(File.Exists(pngPath));
            Assert.False(File.Exists(Path.Combine(extracted, "Art", "kv2.generated")));

            string atlas = File.ReadAllText(atlasPath);
            Assert.True(atlas.Length > 200);

            var texture=Wagenheimer.MsxGraphics.Formats.PngRgba.Decode(File.ReadAllBytes(pngPath));
            var regions=Wagenheimer.MsxGraphics.Formats.AtlasInventory.Read(atlas,texture);
            Assert.All(regions,r=>Assert.True(r.VisiblePixels>0,r.Name+" is transparent"));
            foreach(string name in new[]{"vick_s0_00","vick_s6_13","vick_x_06","vick_d_06","font_A","digit_9","door_clean_msx1_0"})
                Assert.Contains(regions,r=>r.Name==name);
            Assert.DoesNotContain(regions,r=>r.Name=="vick_d_15" || r.Name=="enemy_t5_15" || r.Name=="hud_strip");
            for(int type=1;type<=4;type++) for(int frame=0;frame<26;frame++)
                Assert.Contains(regions,r=>r.Name==$"enemy_t{type}_{frame:00}");
            Assert.Equal(104,regions.Count(r=>r.Name.StartsWith("enemy_t",StringComparison.Ordinal)));
            foreach(int frame in new[]{4,6,8,10,12,14})
                Assert.Contains(regions,r=>r.Name==$"proj_{frame:00}");
            Assert.Equal(6,regions.Count(r=>r.Name.StartsWith("proj_",StringComparison.Ordinal)));
            for(int pyramid=1;pyramid<=6;pyramid++)
            {
                foreach(string state in new[]{"coffin","coffin_ajar","coffin_open"})
                    Assert.Contains(regions,r=>r.Name==$"{state}_p{pyramid}");
                for(int code=6;code<24;code++) Assert.Contains(regions,r=>r.Name==$"crumble_{code}_p{pyramid}");
                foreach(string state in new[]{"door_f0","door_f1","door_f2","door_stud","ladder","ladder_alt","movable_block"})
                    Assert.Contains(regions,r=>r.Name==$"{state}_p{pyramid}");
                for(int variant=0;variant<4;variant++)
                {
                    Assert.Contains(regions,r=>r.Name==$"soft_p{pyramid}_{variant}");
                    Assert.Contains(regions,r=>r.Name==$"wall_p{pyramid}_{variant}");
                }
            }
            byte[] sourceRom=File.ReadAllBytes(romPath);
            var source=RomScreens.CaptureGameplay(sourceRom);
            int patternBase=(source.Registers[6]&7)<<11;
            for(int frame=0;frame<6;frame++)
            {
                var region=Assert.Single(regions,r=>r.Name==$"proj_{4+frame*2:00}");
                int pattern=sourceRom[2*8192+0x8FAF+frame-0x8000]&0xFC;
                byte colorIndex=sourceRom[2*8192+0x8FAC+(frame<3?1:2)-0x8000];
                var color=Wagenheimer.MsxGraphics.Video.Tms9918.Palette[colorIndex];
                for(int y=0;y<16;y++) for(int x=0;x<16;x++)
                {
                    bool set=(source.Vram[patternBase+pattern*8+(x/8)*16+y]&(128>>(x%8)))!=0;
                    int offset=((region.Y+y)*texture.Width+region.X+x)*4;
                    Assert.Equal(set?(byte)255:(byte)0,texture.Pixels[offset+3]);
                    if(!set) continue;
                    Assert.Equal(color.R,texture.Pixels[offset]);
                    Assert.Equal(color.G,texture.Pixels[offset+1]);
                    Assert.Equal(color.B,texture.Pixels[offset+2]);
                }
            }
            foreach(var region in regions.Where(r=>r.Name.StartsWith("enemy_t",StringComparison.Ordinal)))
            {
                int type=region.Name[7]-'0';
                var color=Wagenheimer.MsxGraphics.Video.Tms9918.Palette[type==1?15:type==4?9:10];
                for(int y=0;y<region.Height;y++) for(int x=0;x<region.Width;x++)
                {
                    int offset=((region.Y+y)*texture.Width+region.X+x)*4;
                    if(texture.Pixels[offset+3]==0) continue;
                    Assert.Equal(color.R,texture.Pixels[offset]);
                    Assert.Equal(color.G,texture.Pixels[offset+1]);
                    Assert.Equal(color.B,texture.Pixels[offset+2]);
                }
            }

            byte[] png = File.ReadAllBytes(pngPath);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyMixedCacheCannotEstablishEitherFlavorAvailability()
    {
        string root = NewTempDirectory();
        string originalContent = RomContent.ContentDirectory;
        string? originalOverride = AudioBootstrap.ExtractedDirectoryOverrideForTests;
        ArtSet originalActive = RomContent.Active;

        try
        {
            // A stale MSX2 install directory keeps the MSX2 set in the cycle, but it has no art or murals,
            // so it is not playable. Only the extracted MSX1 files can actually run.
            string content = Path.Combine(root, "Content");
            string extracted = Path.Combine(root, "extracted");
            Directory.CreateDirectory(Path.Combine(content, "Rom", "Art"));
            Directory.CreateDirectory(Path.Combine(extracted, "Levels"));
            Directory.CreateDirectory(Path.Combine(extracted, "Screens"));
            Directory.CreateDirectory(Path.Combine(extracted, "Rooms", "dumps"));
            Directory.CreateDirectory(Path.Combine(extracted, "Art"));

            File.WriteAllText(Path.Combine(extracted, "Levels", "stage01.json"), "{\"stage\":1}");
            File.WriteAllBytes(Path.Combine(extracted, "Screens", "title.bin"), new byte[24614]);
            File.WriteAllBytes(Path.Combine(extracted, "Rooms", "dumps", "s01_r01.bin"), new byte[24614]);
            File.WriteAllText(Path.Combine(extracted, "Art", "kv2.atlas"),
                "vick_s0_00\n1,1,16,32\n0,0\n" + new string('x', 400));

            AudioBootstrap.ExtractedDirectoryOverrideForTests = extracted;

            // Simulate the stale save from a previous MSX2 test run.
            RomContent.Initialize(content);
            RomContent.Use(ArtSets.Msx2Id);
            RomContent.Initialize(content);

            Assert.False(RomContent.AreStagesAvailable);
            Assert.False(RomContent.IsMsx1Available);
            Assert.False(RomContent.IsMsx2Available);
            Assert.False(RomContent.CanPlayAny);
            Assert.False(RomContent.IsAvailable);
            Assert.NotEqual(Path.Combine(extracted, "Art", "kv2.atlas"), RomContent.AtlasPath);
        }
        finally
        {
            AudioBootstrap.ExtractedDirectoryOverrideForTests = originalOverride;
            RomContent.Initialize(originalContent);
            RomContent.Use(originalActive);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Msx1DoesNotBorrowTheBundledMsx2Atlas()
    {
        string root = NewTempDirectory();
        string originalContent = RomContent.ContentDirectory;
        string? originalOverride = AudioBootstrap.ExtractedDirectoryOverrideForTests;
        ArtSet originalActive = RomContent.Active;

        try
        {
            string content = Path.Combine(root, "Content");
            string extracted = Path.Combine(root, "extracted");
            string bundledAtlas = Path.Combine(content, "Rom", "Art", "kv2.atlas");
            string generatedAtlas = Path.Combine(extracted, "Art", "kv2.atlas");

            Directory.CreateDirectory(Path.GetDirectoryName(bundledAtlas)!);
            Directory.CreateDirectory(Path.GetDirectoryName(generatedAtlas)!);
            Directory.CreateDirectory(Path.Combine(extracted, "Screens"));
            Directory.CreateDirectory(Path.Combine(extracted, "Rooms", "dumps"));
            File.WriteAllText(bundledAtlas, "vick_s0_00\n1,1,16,32\n0,0\n" + new string('x', 400));
            File.WriteAllText(generatedAtlas, "vick_s0_00\n1,1,16,32\n0,0\n" + new string('y', 400));
            File.WriteAllText(Path.Combine(extracted, "Art", "kv2.generated"), "generated-msx1-fallback\n");
            File.WriteAllBytes(Path.Combine(extracted, "Screens", "title.bin"), new byte[24614]);
            File.WriteAllBytes(Path.Combine(extracted, "Rooms", "dumps", "s01_r01.bin"), new byte[24614]);

            AudioBootstrap.ExtractedDirectoryOverrideForTests = extracted;
            RomContent.Initialize(content);

            RomContent.Use(new ArtSet("msx1","MSX1",Path.Combine(content,"RomMsx1"),ArtFlavor.Msx1,ExtractedFirst:true));
            Assert.NotEqual(bundledAtlas, RomContent.AtlasPath);
            Assert.False(RomContent.IsMsx1Available);
        }
        finally
        {
            AudioBootstrap.ExtractedDirectoryOverrideForTests = originalOverride;
            RomContent.Initialize(originalContent);
            RomContent.Use(originalActive);
            Directory.Delete(root, recursive: true);
        }
    }
}
