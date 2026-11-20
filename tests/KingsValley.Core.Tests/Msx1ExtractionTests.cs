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
/// End-to-end checks for the free build: the MSX1 cartridge alone must produce native room dumps, a complete
/// sprite atlas, and enough state for <see cref="RomContent"/> to select a playable art set.
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
    public void SpriteAtlas_CoversEveryNameTheRendererAsksFor()
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
            Assert.True(File.Exists(Path.Combine(extracted, "Art", "kv2.generated")));

            string atlas = File.ReadAllText(atlasPath);
            Assert.True(atlas.Length > 200);

            string[] required =
            {
                "vick_s0_00", "vick_s5_15", "vick_x_00", "vick_d_15",
                "enemy_t0_00", "enemy_t5_15", "coffin_p1", "coffin_ajar_p6", "coffin_open_p3",
                "soulstone_p6", "door_f0_p1", "door_stud_p6", "proj_04", "proj_14", "ladder_p1",
                "knife", "item_drill", "font_A", "digit_9", "font_excl", "arrow",
                "title_cursor", "hud_strip", "wall_p1_0", "soft_p6_3", "crumble_0_p1",
            };

            foreach (string name in required)
                Assert.Contains(name + "\n", atlas, StringComparison.Ordinal);

            byte[] png = File.ReadAllBytes(pngPath);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StaleMsx2Selection_FallsBackToPlayableMsx1AfterExtraction()
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

            Assert.True(RomContent.AreStagesAvailable);
            Assert.True(RomContent.IsMsx1Available);
            Assert.False(RomContent.IsMsx2Available);
            Assert.True(RomContent.CanPlayAny);
            Assert.True(RomContent.IsAvailable);
            Assert.Equal(ArtFlavor.Msx1, RomContent.GetFirstPlayableFlavor());
            Assert.Equal(ArtSets.Msx1Id, RomContent.Active.Id);
            Assert.Equal(Path.Combine(extracted, "Art", "kv2.atlas"), RomContent.AtlasPath);
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
    public void GeneratedFallbackAtlas_YieldsToBundledOriginalArt()
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

            Assert.Equal(bundledAtlas, RomContent.AtlasPath);
            Assert.True(RomContent.IsMsx1Available);
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
