using System;
using System.IO;
using System.Linq;
using KingsValley.Core.Screens;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

/// <summary>
/// The screens must come out of the cartridge exactly as the project ships them: the test boots the ROM
/// in the in-process machine, lets the game draw, decodes the VRAM and compares the result with the
/// <c>.bin</c> that was extracted from an openMSX capture.
/// </summary>
/// <remarks>
/// Needs the ROM on disk; without it the test is a no-op (the ROM is not versioned). Set
/// <c>KINGSVALLEY_TEST_ROM</c> to point at the image.
/// </remarks>
public class RomScreensTests
{
    private static string? FindRom()
    {
        string? configured = Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_ROM");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "kv2-extract", "work", "rom", "kv2_msx1.rom"),
            Path.Combine(Path.GetTempPath(), "opencode", "kv2rom", "kv2_msx1.rom"),
            Path.Combine(Path.GetTempPath(), "kv2rom", "kv2_msx1.rom"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Walks up from the test binary until the shipped MSX1 screen folder is found.</summary>
    private static string? FindScreen(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName,
                "src", "KingsValley.Desktop", "Content", "RomMsx1", "Art", "Screens", file);

            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    [Fact]
    public void Title_IsDrawnByTheCartridgeExactlyAsTheShippedScreen()
    {
        string? rom = FindRom();
        string? reference = FindScreen("title.bin");
        if (rom is null || reference is null)
            return;

        byte[] expected = File.ReadAllBytes(reference);
        ScreenData screen = RomScreens.CaptureTitle(File.ReadAllBytes(rom));
        byte[] actual = ScreenAssembler.Encode(screen.Width, screen.Height, screen.Palette, screen.Indices);

        // Same size, same palette bytes and same pixels.
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Pyramid_IsReachedAndDrawnExactlyAsTheShippedScreen()
    {
        // The state-driven navigation (title -> start select -> pyramid) must land on the same screen
        // the project ships: this is what makes the automatic extraction trustworthy.
        string? rom = FindRom();
        string? reference = FindScreen("pyramid.bin");
        if (rom is null || reference is null)
            return;

        ScreenData screen = RomScreens.CaptureStageIntro(File.ReadAllBytes(rom));
        byte[] actual = ScreenAssembler.Encode(screen.Width, screen.Height, screen.Palette, screen.Indices);

        Assert.Equal(File.ReadAllBytes(reference), actual);
    }

    [Fact]
    public void StateDrivenTitle_MatchesTheFrameDrivenOne()
    {
        // Two ways of waiting for the same screen must agree (no clock involved, everything is frames).
        string? rom = FindRom();
        if (rom is null)
            return;

        byte[] romBytes = File.ReadAllBytes(rom);
        ScreenData byState = RomScreens.CaptureTitle(romBytes);
        ScreenData byFrame = RomScreens.Capture(romBytes, RomScreens.TitleFrame);

        // The state-driven capture keeps the frame with the blinking cursor OFF (the art must be clean:
        // the game draws its own cursor), so it has fewer lit pixels than the frame-based one.
        Assert.True(byState.Indices.Count(i => i != 0) < byFrame.Indices.Count(i => i != 0));
    }

    [Fact]
    public void EnglishTitle_IsAlsoDrawnByTheCartridge()
    {
        string? rom = FindRom();
        if (rom is null)
            return;

        byte[] romBytes = File.ReadAllBytes(rom);
        ScreenData japanese = RomScreens.CaptureTitle(romBytes, TitleVariant.Japanese);
        ScreenData english = RomScreens.CaptureTitle(romBytes, TitleVariant.English);

        // The cartridge really carries two different screens.
        Assert.NotEqual(japanese.Indices, english.Indices);

        // And the English one is the shipped file when it is present.
        string? reference = FindScreen("title_en.bin");
        if (reference is null)
            return;

        byte[] actual = ScreenAssembler.Encode(english.Width, english.Height, english.Palette, english.Indices);
        Assert.Equal(File.ReadAllBytes(reference), actual);
    }

    [Fact]
    public void TitleLanguage_RoundTripsThroughTheSave()
    {
        // The cartridge has both screens; the player picks one in the system menu and it is saved.
        var save = new KingsValley.Core.SaveSystem.SaveGameData();
        Assert.Equal("jp", save.TitleLanguage);
        Assert.Equal(TitleLanguage.Japanese, RomContent.ParseTitleLanguage(save.TitleLanguage));

        save.TitleLanguage = "en";
        Assert.Equal(TitleLanguage.English, RomContent.ParseTitleLanguage(save.TitleLanguage));

        // Anything unexpected falls back to the original (Japanese).
        Assert.Equal(TitleLanguage.Japanese, RomContent.ParseTitleLanguage(null));
        Assert.Equal(TitleLanguage.Japanese, RomContent.ParseTitleLanguage("klingon"));
    }

    [Fact]
    public void DecodeScreen2_ReadsTheTablesFromTheRegisters()
    {
        // A tiny SCREEN 2 layout: names at 0x1800, patterns at 0x0000, colours at 0x2000.
        var vram = new byte[0x4000];
        var registers = new byte[24];
        registers[2] = 0x06; // names  0x1800
        registers[3] = 0xFF; // colour 0x2000, mask 3
        registers[4] = 0x03; // pattern 0x0000, mask 3

        // Top third (ty = 0): tile is code 1
        vram[0x1800] = 0x01;
        vram[0x0008 + 0] = 0b1000_0000;         // pattern bank 0, code 1, row 0: leftmost pixel on
        vram[0x2008 + 0] = 0x4B;                // colour bank 0, code 1, row 0: fg 4, bg 11

        // Middle third (ty = 8, bank 1 at +0x800): tile is code 2
        vram[0x1800 + 8 * 32] = 0x02;
        vram[0x0800 + 16 + 0] = 0b1000_0000;    // pattern bank 1, code 2, row 0: leftmost pixel on
        vram[0x2800 + 16 + 0] = 0x5C;           // colour bank 1, code 2, row 0: fg 5, bg 12

        byte[] indices = RomScreens.DecodeScreen2(vram, registers);

        // Top third
        Assert.Equal(4, indices[0]);
        Assert.Equal(11, indices[1]);

        // Middle third (ty = 8, row = 0, tx = 0)
        int middleOffset = 8 * 8 * RomScreens.Width;
        Assert.Equal(5, indices[middleOffset]);
        Assert.Equal(12, indices[middleOffset + 1]);

        Assert.Equal(RomScreens.Width * RomScreens.Height, indices.Length);
    }

    [Fact]
    public void CapturePlayerPoses_ExtractsValidSprites()
    {
        string? rom = FindRom();
        if (rom is null)
            return;

        byte[] romBytes = File.ReadAllBytes(rom);
        var poses = RomScreens.CapturePlayerPoses(romBytes, frames: 20 * 60);
        Assert.NotEmpty(poses);
    }
}
