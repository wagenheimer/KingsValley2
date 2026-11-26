using Wagenheimer.MsxGraphics.Formats;

namespace KingsValley.Core.Tests;

public sealed class IpsPatchTests
{
    [Fact]
    public void AppliesOverlappingLiteralAndRleRecordsWithoutChangingTheSource()
    {
        byte[] source={1,2,3,4};
        byte[] patch="PATCH"u8.ToArray().Concat(new byte[]{0,0,1,0,2,8,9,0,0,2,0,0,0,3,7}).Concat("EOF"u8.ToArray()).ToArray();
        Assert.Equal(new byte[]{1,8,7,7,7},IpsPatch.Apply(source,patch));
        Assert.Equal(new byte[]{1,2,3,4},source);
    }
    [Fact]
    public void SupportsZeroFilledExtensionAndExplicitFinalTruncation()
    {
        byte[] patch="PATCH"u8.ToArray().Concat(new byte[]{0,0,4,0,1,9}).Concat("EOF"u8.ToArray()).Concat(new byte[]{0,0,3}).ToArray();
        Assert.Equal(new byte[]{1,0,0},IpsPatch.Apply(new byte[]{1},patch));
    }
    [Theory]
    [InlineData("0001020304050607")]
    [InlineData("50415443480000000001")]
    [InlineData("504154434800000000000001")]
    [InlineData("5041544348454F4600")]
    public void RejectsMalformedOrTruncatedInput(string hex)=>Assert.Throws<InvalidDataException>(()=>IpsPatch.Apply(Array.Empty<byte>(),Convert.FromHexString(hex)));
    [Fact]
    public void EnforcesTheOutputBoundBeforeAllocating()
    {
        byte[] patch="PATCH"u8.ToArray().Concat(new byte[]{0,1,0,0,1,7}).Concat("EOF"u8.ToArray()).ToArray();
        Assert.Throws<InvalidDataException>(()=>IpsPatch.Apply(Array.Empty<byte>(),patch,256));
    }
}
