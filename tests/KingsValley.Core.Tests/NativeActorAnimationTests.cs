using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class NativeActorAnimationTests
{
    [Fact]
    public void NativeFrameCadenceKeepsVisibilityPositionsAndBackgroundChanges()
    {
        var animation=new NativeActorAnimation {PoseCount=2,Frames=new(){new(4,80,0,0),new(5,79,1,0),new(0,0,-1,1)}};
        animation.Validate();
        Assert.Equal(new NativeActorFrame(4,80,0,0),animation.At(0));
        Assert.Equal(new NativeActorFrame(5,79,1,0),animation.At(1.0/60));
        Assert.Equal(new NativeActorFrame(0,0,-1,1),animation.At(double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(()=>animation.At(double.NaN));
        animation.Frames[0]=new(0,0,2,0);
        Assert.Throws<InvalidDataException>(animation.Validate);
    }
}
