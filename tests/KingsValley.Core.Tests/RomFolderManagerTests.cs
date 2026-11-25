using System;
using System.IO;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public class RomFolderManagerTests
{
    [Fact]
    public void DefaultRomsDirectory_IsInsideKingsValleyAppData()
    {
        string dir = RomFolderManager.DefaultRomsDirectory;
        Assert.NotNull(dir);
        Assert.Contains("KingsValley", dir);
        Assert.EndsWith("roms", dir);
    }

    [Fact]
    public void EnsureDirectories_CreatesDirectorySuccessfully()
    {
        RomFolderManager.EnsureDirectories();
        Assert.True(Directory.Exists(RomFolderManager.DefaultRomsDirectory));
    }

    [Fact]
    public void KnownSha1Hashes_AreDistinctAndValidLength()
    {
        Assert.Equal(40, RomFolderManager.Msx1Rc760Sha1.Length);
        Assert.Equal(40, RomFolderManager.Msx2Rc761Sha1.Length);
        Assert.Equal(40, RomFolderManager.Msx2Rc761SccSha1.Length);
        Assert.Equal(40, RomFolderManager.Msx2BootHackSha1.Length);

        var hashes = new[]
        {
            RomFolderManager.Msx1Rc760Sha1,
            RomFolderManager.Msx2Rc761Sha1,
            RomFolderManager.Msx2Rc761SccSha1,
            RomFolderManager.Msx2BootHackSha1,
        };

        var unique = new System.Collections.Generic.HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(4, unique.Count);
    }

    [Fact]
    public void InspectRomFile_IdentifiesMsxCartridgeHeuristics()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_msx_{Guid.NewGuid():N}.rom");
        try
        {
            // Create a fake 128 KB cartridge with 'AB' header
            byte[] fakeRom = new byte[131072];
            fakeRom[0] = 0x41; // 'A'
            fakeRom[1] = 0x42; // 'B'
            File.WriteAllBytes(tempFile, fakeRom);

            var info = RomFolderManager.InspectRomFile(tempFile);
            Assert.NotNull(info);
            Assert.Equal(131072, info.SizeBytes);
            Assert.Contains("128 KB", info.Description);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void RomContent_StatusProperties_DoNotThrow()
    {
        bool _ = RomContent.IsMsx1Available;
        bool __ = RomContent.IsMsx2Available;
        bool ___ = RomContent.IsAudioAvailable;
        bool ____ = RomContent.AreStagesAvailable;
        bool canPlay = RomContent.CanPlayAny;

        Assert.True(canPlay || !canPlay); // confirms evaluation doesn't throw
    }
}
