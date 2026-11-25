using KingsValley.Core.Screens;
using Wagenheimer.MsxGraphics.Extraction;
using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Core.Tests;

public sealed class MsxGraphicsTests
{
    [Fact]
    public void GraphicsDestinationsAreSeparateAndDoNotCreateDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "msx-graphics-" + Guid.NewGuid().ToString("N"));
        string msx1 = GraphicsLayout.GetRoot(root, "kings-valley-2", "msx1");
        string msx2 = GraphicsLayout.GetRoot(root, "kings-valley-2", "msx2");
        Assert.Equal(Path.Combine(root, "kings-valley-2", "msx1", "Graphics"), msx1);
        Assert.NotEqual(msx1, msx2);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("../msx1")]
    [InlineData("msx1/msx2")]
    [InlineData("msx1\\msx2")]
    [InlineData("C:")]
    [InlineData("MSX1")]
    [InlineData("")]
    public void UnsafeOrAmbiguousIdentifiersCannotEscapeOrAliasTheVariant(string variant)
        => Assert.Throws<ArgumentException>(() => GraphicsLayout.GetRoot(Path.GetTempPath(), "game", variant));

    [Fact]
    public void OddLengthPixelsUseHighNibbleFirstAndPadOnlyTheUnusedNibble()
    {
        byte[] pixels = { 1, 2, 15 };
        byte[] packed = Packed4Bpp.Pack(pixels);
        Assert.Equal(new byte[] { 0x12, 0xF0 }, packed);
        Assert.Equal(pixels, Packed4Bpp.Unpack(packed, 3));
    }

    [Fact]
    public void InvalidPixelIndicesAreRejectedInsteadOfMaskedIntoAnotherColor()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Packed4Bpp.Pack(new byte[] { 16 }));

    [Fact]
    public void MissingPackedBytesCannotTurnIntoAnApparentlyValidBlackTail()
        => Assert.Throws<InvalidDataException>(() => Packed4Bpp.Unpack(new byte[] { 0x12 }, 4));

    [Fact]
    public void ScreenAdapterRejectsTruncationBeforeAllocatingOrFillingPixels()
    {
        var bytes = ScreenAssembler.Encode(2, 2, new ScreenColor[16], new byte[] { 1, 2, 3, 4 });
        Assert.Throws<InvalidDataException>(() => ScreenAssembler.Decode(bytes[..^1]));
    }

    [Fact]
    public void ScreenAdapterDoesNotPublishDimensionsThatDisagreeWithThePixels()
        => Assert.Throws<ArgumentException>(() => ScreenAssembler.Encode(2, 2, new ScreenColor[16], new byte[] { 1, 2 }));
}
