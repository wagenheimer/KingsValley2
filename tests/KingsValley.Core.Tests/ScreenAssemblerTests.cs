using System.IO;
using KingsValley.Core.Screens;
using Xunit;

namespace KingsValley.Core.Tests;

public class ScreenAssemblerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KingsValley.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("Repo root (KingsValley.slnx) not found.");
        return dir.FullName;
    }

    private static string ScreensDir() =>
        Path.Combine(RepoRoot(), "src", "KingsValley.Desktop", "Content", "Rom", "Art", "Screens");

    [Theory]
    [InlineData("world_map", 256, 212, false)]
    [InlineData("title", 256, 212, false)]
    [InlineData("start_select", 256, 212, false)]
    [InlineData("pyramid", 256, 212, false)]
    [InlineData("intro_msg", 256, 212, false)]
    [InlineData("sound_select", 256, 212, false)]
    [InlineData("sound_select_cursor", 16, 31, true)]
    [InlineData("konami", 168, 48, false)]
    [InlineData("puzzle", 256, 212, false)]
    [InlineData("puzzle_bg", 256, 212, false)]
    [InlineData("puzzle_tiles", 80, 80, false)]
    [InlineData("world_vick", 64, 48, true)]
    public void DecodeFile_LoadsValidScreenData(string name, int expectedW, int expectedH, bool expectedAlpha)
    {
        var path = Path.Combine(ScreensDir(), name + ".bin");
        if (!File.Exists(path))
            return; // Skip when running in a clean open-source repo without pre-extracted ROM binaries

        var data = ScreenAssembler.DecodeFile(path);
        Assert.Equal(expectedW, data.Width);
        Assert.Equal(expectedH, data.Height);
        Assert.Equal(expectedAlpha, data.HasAlpha);
        Assert.Equal(16, data.Palette.Length);
        Assert.Equal(expectedW * expectedH, data.Indices.Length);

        // Checks that all color indices are in the MSX2 0..15 range
        for (int i = 0; i < data.Indices.Length; i++)
            Assert.True(data.Indices[i] <= 15, $"Pixel index out of bounds: {data.Indices[i]}");
    }

    [Fact]
    public void EncodeDecode_RoundtripMatches()
    {
        var pal = new ScreenColor[16];
        for (byte i = 0; i < 16; i++)
            pal[i] = new ScreenColor((byte)(i * 16), (byte)(i * 16), (byte)(i * 16));

        var indices = new byte[] { 1, 2, 3, 4, 15, 0 };
        var encoded = ScreenAssembler.Encode(3, 2, pal, indices, hasAlpha: true);
        var decoded = ScreenAssembler.Decode(encoded);

        Assert.Equal(3, decoded.Width);
        Assert.Equal(2, decoded.Height);
        Assert.True(decoded.HasAlpha);
        Assert.Equal(indices.Length, decoded.Indices.Length);
        for (int i = 0; i < indices.Length; i++)
            Assert.Equal(indices[i], decoded.Indices[i]);
    }
}
