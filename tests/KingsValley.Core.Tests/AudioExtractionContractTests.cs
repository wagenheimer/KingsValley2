using KingsValley.Core.Gameplay;
using KingsValley.Engine.Assets;
using Wagenheimer.MsxAudio;
using Wagenheimer.MsxAudio.Extraction;

namespace KingsValley.Core.Tests;

[Collection(Msx1ExtractionTestCollection.Name)]
public sealed class AudioExtractionContractTests
{
    [Fact]
    public void AudioPackagesDoNotBorrowTheLegacyMixedCacheOrTheOtherVariant()
    {
        string root=Path.Combine(Path.GetTempPath(),"kv2-audio-paths-"+Guid.NewGuid().ToString("N"));
        string? previous=AudioBootstrap.ExtractedDirectoryOverrideForTests;
        try
        {
            Directory.CreateDirectory(root);
            AudioBootstrap.ExtractedDirectoryOverrideForTests=root;
            File.WriteAllText(Path.Combine(root,"audio.msxaudio"),"legacy corrupt package");
            Assert.Equal(MsxAudioPaths.PackagePath("kings-valley-2","msx1",root),AudioBootstrap.AudioPackageFor(ArtFlavor.Msx1));
            Assert.NotEqual(AudioBootstrap.AudioPackageFor(ArtFlavor.Msx1),AudioBootstrap.AudioPackageFor(ArtFlavor.Msx2));
            Assert.False(AudioBootstrap.HasPackage);
        }
        finally { AudioBootstrap.ExtractedDirectoryOverrideForTests=previous;Directory.Delete(root,true); }
    }

    [Theory]
    [InlineData("msx1")]
    [InlineData("msx2")]
    public void EveryGameplaySoundHasARequiredIdAndDocumentedName(string variant)
    {
        var profile=RomExtractionProfiles.FindByGameVariant("kings-valley-2",variant)!;
        Assert.False(profile.BootCartridge);
        Assert.Equal(0x6006,profile.PlayerAddress);
        Assert.Equal(Enumerable.Range(1, 65), profile.Ids);
        Assert.Contains(2, profile.RequiredIds); // audible unused tone
        Assert.Contains(32, profile.RequiredIds); // play-start sound, previously omitted
        Assert.Contains(55, profile.RequiredLoopIds); // sustained ending effect
        Assert.True(profile.DriverObservation!.Required);
        Assert.Equal(8, profile.DriverObservation.ActiveAddresses.Count);
        foreach(var field in typeof(OriginalSounds).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static))
        {
            int id=(int)field.GetRawConstantValue()!;
            Assert.Contains(id,profile.RequiredIds);
            Assert.True(profile.Names.ContainsKey(id),field.Name);
            Assert.True(profile.NameSources.ContainsKey(id),field.Name);
        }
        Assert.DoesNotContain(67,profile.Ids);
        Assert.DoesNotContain(70,profile.Ids);
    }
}
