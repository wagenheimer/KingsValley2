using KingsValley.Engine.UI;

namespace KingsValley.Core.Tests;

public sealed class SystemMenuValueLayoutTests
{
    [Theory]
    [InlineData("GRAPHICS","MSX2 CONTEST")]
    [InlineData("GRAPHICS","MSX2 CONTEST (LOCKED)")]
    [InlineData("GRAPHICS","A VERY LONG CUSTOM GRAPHICS PACK NAME")]
    [InlineData("AUDIO","HEADPHONES CROSSFEED")]
    [InlineData("OBJECT BG","TRANSPARENT")]
    public void LongValuesKeepTheLabelGapAndStayInsideThePanel(string label,string value)
    {
        var layout=SystemMenuValueLayout.Fit(label,value,22,202);
        Assert.True(layout.X-7>=22+label.Length*8+5); // Includes the left adjustment arrow.
        Assert.Equal(202,layout.X+layout.Width);
        Assert.Equal(layout.Text.Length*(layout.Compact?4:8),layout.Width);
    }
    [Fact]
    public void OrdinaryValuesRetainNativeFontAndLongValuesUseCompactFont()
    {
        Assert.False(SystemMenuValueLayout.Fit("GRAPHICS","MSX2 CONTEST",22,202).Compact);
        Assert.True(SystemMenuValueLayout.Fit("GRAPHICS","MSX2 CONTEST (LOCKED)",22,202).Compact);
        Assert.EndsWith("...",SystemMenuValueLayout.Fit("GRAPHICS",new string('X',100),22,202).Text);
    }
}
