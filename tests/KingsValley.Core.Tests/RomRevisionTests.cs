using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using KingsValley.Engine.Extraction;
using Wagenheimer.MsxAudio.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public sealed class RevisionCorpusFactAttribute : FactAttribute
{
    public RevisionCorpusFactAttribute()
    {
        if (!Directory.Exists(Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_REVISIONS_DIR")))
            Skip = "Set KINGSVALLEY_TEST_REVISIONS_DIR to a private corpus with <sha1>.rom files; no ROMs are distributed.";
    }
}

public class RomRevisionTests
{
    [Theory]
    [InlineData("msx1", "f3306e6f25d111da21ce66db3404f5f48acb25a1", 0x7E40)]
    [InlineData("msx2", "f25628e474035e6bf0440b274c5df04f94c49fb8", 0x7F10)]
    public void SlotTwoConversionsHaveTheirOwnHardwareAndRamHelper(string variant, string hash, int offset)
    {
        var recipe = Assert.Single(RomExtractionProfiles.FindAllByGameVariant("kings-valley-2", variant),
            profile => profile.RomSha1.Contains(hash));
        Assert.Equal(RomMapper.Ascii8, RomMapperNames.Parse(recipe.RomType));
        Assert.Equal(2, recipe.ExternalSccSlot);
        var copy = Assert.Single(recipe.MemoryCopies);
        Assert.Equal(offset, copy.RomOffset); Assert.Equal(0xF87F, copy.Address); Assert.Equal(32, copy.Length);
        Assert.DoesNotContain(hash, RomExtractionProfiles.FindByGameVariant("kings-valley-2", variant)!.RomSha1);
    }

    [RevisionCorpusFact]
    public void EntireCorpusResolvesExactHardwareAndStageInventoriesWithoutChangingSources()
    {
        string corpus = Environment.GetEnvironmentVariable("KINGSVALLEY_TEST_REVISIONS_DIR")!;
        string output = Path.Combine(Path.GetTempPath(), "kv2-revision-stages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            foreach (var entry in SupportedCartridges.All)
            {
                byte[] source = File.ReadAllBytes(Path.Combine(corpus, entry.Sha1 + ".rom"));
                Assert.Equal(entry.Sha1, Convert.ToHexString(SHA1.HashData(source)).ToLowerInvariant());
                byte[] before = (byte[])source.Clone();
                var profile = RomExtractionProfiles.FindForRom(source)!;
                Assert.Equal(entry.Variant, profile.Variant);
                Assert.True(RomFolderManager.SupportsSha1(entry.Sha1));
                if (entry.Sha1 == "f25628e474035e6bf0440b274c5df04f94c49fb8")
                {
                    byte[] execution = Msx2ExecutionImage.For(source);
                    Assert.Equal("0c7e0b618c72e7ca08bd756c3103b46e5c580c04", Convert.ToHexString(SHA1.HashData(execution)).ToLowerInvariant());
                    Assert.True(source.AsSpan(0x8000).SequenceEqual(execution.AsSpan(0x8000)));
                    Assert.Equal(before, source);
                }
                string stages = Path.Combine(output, entry.Sha1);
                int count = entry.Variant == "msx2-contest" ? 20 : 60;
                if (entry.Variant == "msx2-contest") ContestCartridge.WriteStages(source, stages, count);
                else RomStageJson.WriteAll(source, stages);
                Assert.Equal(count, Directory.GetFiles(stages, "stage*.json").Length);
                Assert.Equal(before, source);
                if (entry.Sha1 == ContestEnglishPatch.SourceSha1 || entry.Sha1 == ContestEnglishPatch.OriginalSourceSha1)
                {
                    using var resource = typeof(ContestEnglishPatch).Assembly.GetManifestResourceStream("KingsValley.FRS.Patches.zip")!;
                    using var zip = new System.IO.Compression.ZipArchive(resource);
                    using var patchStream = zip.GetEntry("KVAL2G.IPS")!.Open();
                    using var patch = new MemoryStream(); patchStream.CopyTo(patch);
                    string expected = entry.Sha1 == ContestEnglishPatch.SourceSha1 ? ContestEnglishPatch.EnglishSha1 : ContestEnglishPatch.OfficialEnglishSha1;
                    Assert.Equal(expected, Convert.ToHexString(SHA1.HashData(ContestEnglishPatch.Apply(source, patch.ToArray()))).ToLowerInvariant());
                    Assert.Equal(before, source);
                }
            }
            foreach (var pair in new[] { (ContestEnglishPatch.SourceSha1, ContestEnglishPatch.EnglishSha1), (ContestEnglishPatch.OriginalSourceSha1, ContestEnglishPatch.OfficialEnglishSha1) })
                for (int stage = 1; stage <= 20; stage++)
                    Assert.Equal(File.ReadAllText(Path.Combine(output, pair.Item1, $"stage{stage:00}.json")),
                        File.ReadAllText(Path.Combine(output, pair.Item2, $"stage{stage:00}.json")));
        }
        finally { Directory.Delete(output, true); }
    }
}
