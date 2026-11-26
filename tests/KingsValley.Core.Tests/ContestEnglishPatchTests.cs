using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using KingsValley.Engine.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public class ContestEnglishPatchTests
{
    [Fact]
    public void BundledArchiveIsCompleteAndUnmodified()
    {
        using var stream = typeof(ContestEnglishPatch).Assembly.GetManifestResourceStream("KingsValley.FRS.Patches.zip");
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        Assert.Equal(ContestEnglishPatch.ArchiveSha256, Convert.ToHexString(SHA256.HashData(buffer.ToArray())));
        using var zip = new ZipArchive(new MemoryStream(buffer.ToArray()));
        Assert.NotNull(zip.GetEntry("README.TXT"));
        Assert.NotNull(zip.GetEntry("KVAL2G.IPS"));
        Assert.NotNull(zip.GetEntry("KVAL21.IPS"));
        Assert.NotNull(zip.GetEntry("KVAL22.IPS"));
        Assert.NotNull(zip.GetEntry("KV2STAGE.COM"));
    }

    [Fact]
    public void WrongCartridgeCannotBePatched()
    {
        var source = new byte[131072];
        Assert.Throws<InvalidDataException>(() => ContestEnglishPatch.Apply(source, Array.Empty<byte>()));
        Assert.All(source, value => Assert.Equal(0, value));
    }
}
