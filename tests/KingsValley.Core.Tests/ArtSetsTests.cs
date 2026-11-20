using System;
using System.IO;
using System.Linq;
using KingsValley.Engine.Assets;
using Xunit;

namespace KingsValley.Core.Tests;

/// <summary>
/// The art sets are what makes an original or a remaster a **folder** instead of a code change:
/// discovery must find the built-ins, pick up a pack dropped in <c>Content/ArtSets/</c>, and let a
/// pack inherit whatever it did not redo.
/// </summary>
public class ArtSetsTests : IDisposable
{
    private readonly string _content;

    public ArtSetsTests()
    {
        _content = Path.Combine(Path.GetTempPath(), "kv-artsets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_content))
                Directory.Delete(_content, recursive: true);
        }
        catch (IOException)
        {
            // best effort: the temp folder is disposable anyway
        }
    }

    private string Folder(params string[] parts)
    {
        string path = Path.Combine(new[] { _content }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string File_(params string[] parts)
    {
        string path = Path.Combine(new[] { _content }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Discover_FindsTheOriginalMsx2()
    {
        Folder("Rom", "Art");

        var sets = ArtSets.Discover(_content);

        ArtSet set = Assert.Single(sets);
        Assert.Equal(ArtSets.Msx2Id, set.Id);
        Assert.Equal(ArtFlavor.Msx2, set.Flavor);
        Assert.Equal(Path.Combine(_content, "Rom"), set.Root);
    }

    [Fact]
    public void Discover_ListsBothOriginalsInMenuOrder()
    {
        Folder("Rom", "Art");
        Folder("RomMsx1", "Art");

        var sets = ArtSets.Discover(_content);

        Assert.Equal(new[] { "msx2", "msx1" }, sets.Select(s => s.Id));
        Assert.Equal(ArtFlavor.Msx1, sets[1].Flavor);
        Assert.True(sets[1].ExtractedFirst, "the MSX1 set reads the screens extracted from the ROM");
    }

    [Fact]
    public void Discover_KeepsMsx1WhenOnlyTheExtractedScreensExist()
    {
        // The free build ships no Content/Rom*: the first boot writes the extracted screens.
        Folder("Rom", "Art");
        string extracted = Folder("extracted", "Screens");

        var sets = ArtSets.Discover(_content, extracted);

        Assert.Contains(sets, s => s.Id == ArtSets.Msx1Id && s.ExtractedFirst);
    }

    [Fact]
    public void Discover_PicksUpARemasterDroppedInArtSets()
    {
        Folder("Rom", "Art");
        Folder("ArtSets", "remaster", "Art");
        File_( "ArtSets", "remaster", "artset.json");
        File.WriteAllText(Path.Combine(_content, "ArtSets", "remaster", "artset.json"),
            """{ "name": "Remaster 4K", "flavor": "msx2", "base": "msx2" }""");

        var sets = ArtSets.Discover(_content);

        ArtSet pack = Assert.Single(sets, s => s.Id == "remaster");
        Assert.Equal("Remaster 4K", pack.Name);
        Assert.Equal(ArtFlavor.Msx2, pack.Flavor);
        Assert.Equal(ArtSets.Msx2Id, pack.BaseId);
        Assert.Equal(Path.Combine(_content, "ArtSets", "remaster"), pack.Root);
    }

    [Fact]
    public void Discover_RemasterWithoutManifestStillWorks()
    {
        Folder("Rom", "Art");
        Folder("ArtSets", "hd", "Art");

        ArtSet pack = Assert.Single(ArtSets.Discover(_content), s => s.Id == "hd");

        Assert.Equal("HD", pack.Name);            // the folder name, upper-cased
        Assert.Equal(ArtFlavor.Msx2, pack.Flavor); // default pipeline
        Assert.Equal(ArtSets.Msx2Id, pack.BaseId); // default base
    }

    [Fact]
    public void Discover_RemasterCanFollowTheMsx1Pipeline()
    {
        Folder("Rom", "Art");
        Folder("ArtSets", "remaster-msx1", "Art");
        File.WriteAllText(Path.Combine(_content, "ArtSets", "remaster-msx1", "artset.json"),
            """{ "name": "Remaster MSX1", "flavor": "msx1", "base": "msx1" }""");

        ArtSet pack = Assert.Single(ArtSets.Discover(_content), s => s.Id == "remaster-msx1");

        Assert.Equal(ArtFlavor.Msx1, pack.Flavor);
        Assert.Equal(ArtSets.Msx1Id, pack.BaseId);
    }

    [Fact]
    public void Discover_IgnoresAFolderWithoutArt()
    {
        Folder("Rom", "Art");
        Folder("ArtSets", "empty");

        Assert.DoesNotContain(ArtSets.Discover(_content), s => s.Id == "empty");
    }

    [Fact]
    public void Resolve_TakesTheActiveSetAndFallsBackToTheBase()
    {
        Folder("Rom", "Art", "Screens");
        string inBase = File_("Rom", "Art", "Screens", "title.bin");

        Folder("ArtSets", "remaster", "Art", "Screens");
        string inPack = File_("ArtSets", "remaster", "Art", "Screens", "pyramid.bin");

        var sets = ArtSets.Discover(_content);
        ArtSet pack = sets.Single(s => s.Id == "remaster");

        // The remaster only redid the pyramid; the title still comes from the original.
        Assert.Equal(inPack, ArtSets.Resolve(sets, pack, Path.Combine("Art", "Screens", "pyramid.bin")));
        Assert.Equal(inBase, ArtSets.Resolve(sets, pack, Path.Combine("Art", "Screens", "title.bin")));
    }

    [Fact]
    public void Find_FallsBackToTheFirstSetWhenTheIdIsUnknown()
    {
        Folder("Rom", "Art");
        var sets = ArtSets.Discover(_content);

        Assert.Equal(ArtSets.Msx2Id, ArtSets.Find(sets, "does-not-exist")!.Id);
        Assert.Equal(ArtSets.Msx2Id, ArtSets.Find(sets, null)!.Id);
    }
}
