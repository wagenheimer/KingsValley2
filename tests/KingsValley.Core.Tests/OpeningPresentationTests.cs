using KingsValley.Core.Screens;

namespace KingsValley.Core.Tests;

public class OpeningPresentationTests
{
    [Theory]
    [InlineData(192,10)]
    [InlineData(212,0)]
    public void FullNativeLogoRevealsItsArtWithoutAddingTheOldCropOrigin(int height,int offset)
    {
        var frame=OpeningPresentation.At(1.5,height);
        Assert.Equal(offset,frame.OffsetY);
        Assert.Equal(109,frame.SourceHeight); // Native art begins at row 64; 45 rows have been revealed.
        Assert.False(frame.EnterMenu);
        Assert.Equal(112,OpeningPresentation.At(2,height).SourceHeight);
    }

    [Fact]
    public void TheCompletedLogoEntersTheInteractiveMenuWithoutASecondStaticTitleWait()
    {
        Assert.False(OpeningPresentation.At(5.89,192).EnterMenu);
        Assert.True(OpeningPresentation.At(5.9,192).EnterMenu);
        Assert.True(OpeningPresentation.At(0.15,212,fast:true).EnterMenu);
    }
}
