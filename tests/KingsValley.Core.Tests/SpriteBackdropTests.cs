using Wagenheimer.MsxGraphics.Imaging;
using KingsValley.Engine.Graphics;

namespace KingsValley.Core.Tests;

public class SpriteBackdropTests
{
    [Theory]
    [InlineData("item_knife",true)]
    [InlineData("knife",true)]
    [InlineData("drill",true)]
    [InlineData("coffin_open_p1",true)]
    [InlineData("soulstone_p1",true)]
    [InlineData("door_f0_p1",true)]
    [InlineData("vick_d_00",false)]
    [InlineData("soft_p1_0",false)]
    [InlineData("crumble_6_p1",false)]
    public void OptionalObjectTransparencyDoesNotAlterActorsOrTerrain(string name,bool eligible)=>
        Assert.Equal(eligible,Msx1ObjectPresentation.HasBlackBackdrop(name));
    [Fact]
    public void RemovesOnlyBorderConnectedBlackAndRetainsEnclosedBlackDetail()
    {
        byte[] pixels=new byte[5*5*4];
        for(int i=0;i<25;i++)pixels[i*4+3]=255;
        for(int y=1;y<=3;y++)for(int x=1;x<=3;x++)
            if(x!=2 || y!=2){int p=(y*5+x)*4;pixels[p]=80;pixels[p+1]=120;pixels[p+2]=160;}
        byte[] source=(byte[])pixels.Clone();
        var clear=SpriteBackdrop.RemoveConnectedBlack(pixels,5,5);
        Assert.Equal(0,clear[3]);Assert.Equal(255,clear[(2*5+2)*4+3]);
        Assert.Equal(source,pixels);Assert.Equal(clear,SpriteBackdrop.RemoveConnectedBlack(clear,5,5));
        Assert.Equal(new byte[]{80,120,160,255},clear.AsSpan((1*5+1)*4,4).ToArray());
    }
}
