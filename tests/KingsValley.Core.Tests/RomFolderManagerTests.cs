using System;
using System.IO;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public class RomFolderManagerTests
{
    [Fact]
    public void ProfileAliasesStayDistinctFromUnverifiedCartridges()
    {
        Assert.True(RomFolderManager.SupportsSha1("cb612cbfdf79dabe55164b386ca956a3e187bef7"));
        Assert.False(RomFolderManager.SupportsSha1(new string('0',40)));
    }

    [Fact]
    public void VerifiedFrsMsx1Revision_HasTheSharedAudioAndGraphicsProfile()
    {
        const string hash="cb612cbfdf79dabe55164b386ca956a3e187bef7";
        var profile=Wagenheimer.MsxAudio.Extraction.RomExtractionProfiles.FindByGameVariant("kings-valley-2","msx1");
        Assert.NotNull(profile);
        Assert.Contains(hash,profile.RomSha1,StringComparer.OrdinalIgnoreCase);
        Assert.True(RomFolderManager.SupportsSha1(hash));
    }

    [Fact]
    public void Reset_RemovesBothVariantsAndPreservesRomsReportsAndOtherGames()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv2-reset-"+Guid.NewGuid().ToString("N"));
        try
        {
            foreach(var variant in new[]{"msx1","msx2"})
            foreach(var component in new[]{"Graphics","Audio","Report"})
            {
                string dir=Path.Combine(root,"kings-valley-2",variant,component);
                Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"test.data"),"test");
            }
            Directory.CreateDirectory(Path.Combine(root,"another-game"));
            File.WriteAllText(Path.Combine(root,"original.rom"),"source");
            Assert.True(RomFolderManager.PurgeExtractedContent(root));
            foreach(var variant in new[]{"msx1","msx2"})
            {
                Assert.False(Directory.Exists(Path.Combine(root,"kings-valley-2",variant,"Graphics")));
                Assert.False(Directory.Exists(Path.Combine(root,"kings-valley-2",variant,"Audio")));
                Assert.True(File.Exists(Path.Combine(root,"kings-valley-2",variant,"Report","test.data")));
            }
            Assert.True(Directory.Exists(Path.Combine(root,"another-game")));
            Assert.Equal("source",File.ReadAllText(Path.Combine(root,"original.rom")));
            Assert.False(RomFolderManager.PurgeExtractedContent(root));
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }

    [Fact]
    public void ScanRoms_FindsCartridgesBesideTheExecutable()
    {
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,$"portable-{Guid.NewGuid():N}.rom");
        try
        {
            byte[] cartridge=new byte[131072];cartridge[0]=0x41;cartridge[1]=0x42;
            File.WriteAllBytes(path,cartridge);
            Assert.Contains(RomFolderManager.ScanRoms(),r=>Path.GetFullPath(r.FilePath)==path);
        }
        finally {if(File.Exists(path))File.Delete(path);}
    }
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
            Assert.False(info.IsSupported);
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
