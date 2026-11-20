using KingsValley.Engine.Scenes;
using Xunit;

namespace KingsValley.Core.Tests;

public class WorldTourTests
{
    [Fact]
    public void WorldTour_StartsAtCorrectCoordinatesForWorld1()
    {
        var (x, y, dir) = WorldTour.StateAt(1, 0);
        // Table 0x71CA: byte 0 (Y) = 43, byte 1 (X) = 78
        Assert.Equal(78, x);
        Assert.Equal(43, y);
        Assert.Equal(2, dir); // starts going left (dir 2)
    }

    [Fact]
    public void WorldTour_ReachesEndInWorld1()
    {
        int steps = WorldTour.StepsOf(1);
        Assert.Equal(88, steps);

        var (x, y, _) = WorldTour.StateAt(1, steps);
        // At the end of world 1's 88 steps, it arrives at X=56, Y=59 (table 0x71D4 of the ROM)
        Assert.Equal(56, x);
        Assert.Equal(59, y);
    }

    [Theory]
    [InlineData(1, 78, 43)]
    [InlineData(2, 46, 59)]
    [InlineData(3, 48, 99)]
    [InlineData(4, 83, 102)]
    [InlineData(5, 94, 83)]
    public void WorldTour_StartsAtCorrectTableCoordinates(int world, int expectedX, int expectedY)
    {
        var (x, y) = WorldTour.PositionAt(world, 0);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }
}
