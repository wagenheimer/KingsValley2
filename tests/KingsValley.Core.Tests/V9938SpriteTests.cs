using Wagenheimer.MsxGraphics.Video;

namespace KingsValley.Core.Tests;

public class V9938SpriteTests
{
    [Fact]
    public void Mode2CombinesFollowingColorPlanesButKeepsEarlierGroupPriority()
    {
        byte[] vram=new byte[131072],regs=new byte[8],colors=new byte[48];regs[1]=2;
        byte[] attributes={19,30,0,0,19,30,4,0,19,30,8,0};
        vram[0]=0xC0;vram[32]=0xA0;vram[64]=0xF0;
        colors[0]=2;colors[16]=0x44;colors[32]=8;
        var pixels=V9938Sprites.DecodeRegion(vram,regs,attributes,colors,30,20,4,1);
        Assert.Equal(new byte[]{6,2,4,8},pixels);
    }

    [Fact]
    public void Mode2UsesPerLineEarlyClockAndSuppressesAnOrphanColorPlane()
    {
        byte[] vram=new byte[131072],regs=new byte[8],colors=new byte[16];regs[1]=2;
        byte[] attributes={19,40,0,0};vram[0]=0x80;vram[1]=0x80;
        colors[0]=0x82;colors[1]=0x44;
        var pixels=V9938Sprites.DecodeRegion(vram,regs,attributes,colors,8,20,1,2);
        Assert.Equal(new byte[]{2,0},pixels);
        Assert.Throws<ArgumentException>(()=>V9938Sprites.DecodeRegion(vram,regs,attributes,new byte[15],8,20,1,1));
    }
}
