using System;
using System.IO;
using KingsValley.Engine.Assets;
using Xunit;

namespace KingsValley.Core.Tests;

public class AtlasFileTests
{
    [Fact]
    public void Parse_ReadsSimplifiedAtlasWithoutHeader()
    {
        var entries = AtlasFile.Parse(new[]
        {
            "vick_s0_00",
            "1,1,16,32",
            "0,0",
            "font_A",
            "20,1,8,8",
            "0.5,0.25",
        });

        Assert.Equal(2, entries.Count);
        Assert.Equal("vick_s0_00", entries[0].Name);
        Assert.Equal(1, entries[0].X);
        Assert.Equal(16, entries[0].Width);
        Assert.Equal(32, entries[0].Height);
        Assert.Equal(0f, entries[0].OriginX);
        Assert.Equal("font_A", entries[1].Name);
        Assert.Equal(8, entries[1].Width);
        Assert.Equal(0.5f, entries[1].OriginX);
        Assert.Equal(0.25f, entries[1].OriginY);
    }

    [Fact]
    public void Parse_AcceptsPngHeaderAndBlankLines()
    {
        var entries = AtlasFile.Parse(new[]
        {
            "kv2.png",
            "",
            "vick_s0_00",
            "1,1,16,32",
            "0,0",
            "",
            "vick_s0_01",
            "18,1,16,32",
            "0,0",
        });

        Assert.Equal(new[] { "vick_s0_00", "vick_s0_01" }, new[] { entries[0].Name, entries[1].Name });
        Assert.Equal(18, entries[1].X);
    }

    [Fact]
    public void Read_UsesAbsolutePathAndHandlesMalformedTrailer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"kv2-atlas-{Guid.NewGuid():N}.atlas");
        try
        {
            File.WriteAllText(path, string.Join('\n', new[]
            {
                "sprite_ok",
                "4,8,16,24",
                "0,0",
                "broken",
                "not,a,rect",
                "0,0",
            }));

            var entries = AtlasFile.Read(path);
            Assert.Single(entries);
            Assert.Equal("sprite_ok", entries[0].Name);
            Assert.Equal(4, entries[0].X);
            Assert.Equal(8, entries[0].Y);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_ExtractedAtlasContainsAllRendererFallbacks()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string path = Path.Combine(home, "Library", "Application Support", "KingsValley",
            "extracted", "Art", "kv2.atlas");
        if (!File.Exists(path))
            return; // MSX1 extraction is not required for the unit test suite.

        var entries = AtlasFile.Read(path);
        Assert.True(entries.Count > 500, $"atlas has only {entries.Count} sprites");
        Assert.Contains(entries, e => e.Name == "vick_s0_00");
        Assert.Contains(entries, e => e.Name == "vick_s5_15");
        Assert.Contains(entries, e => e.Name == "enemy_t0_00");
        Assert.Contains(entries, e => e.Name == "coffin_p1");
        Assert.Contains(entries, e => e.Name == "soulstone_p1");
        Assert.Contains(entries, e => e.Name == "font_A");
        Assert.Contains(entries, e => e.Name == "digit_0");
    }
}
