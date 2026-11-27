using FluentAssertions;
using vos.Service.Watershed.LandCover;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class WorldCoverTilesTests
{
    [Theory]
    [InlineData(25.1488, 55.5468, 25.1576, 55.5692, new[] { "N24E054" })]
    [InlineData(-2.6, -58.2, -2.4, -58.1, new[] { "S03W060" })]
    [InlineData(25, 56.9, 25.1, 57.1, new[] { "N24E054", "N24E057" })]
    [InlineData(26.9, 56.9, 27.1, 57.1, new[] { "N24E054", "N24E057", "N27E054", "N27E057" })]
    [InlineData(-0.1, -0.1, 0.1, 0.1, new[] { "S03W003", "S03E000", "N00W003", "N00E000" })]
    public void A_box_touches_each_tile_named_by_its_south_west_corner(double south, double west, double north, double east, string[] named)
    {
        WorldCoverTiles.Touching(south, west, north, east).Should().Equal(named);
    }
}
