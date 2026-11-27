using FluentAssertions;
using vos.Service.Watershed.LandCover;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class LandCoverSharesTests
{
    private const double Pixel = BuiltTile.DegreesPerPixel;

    private static readonly LandCoverClassCode Trees = new(Guid.NewGuid(), 10);
    private static readonly LandCoverClassCode Bare = new(Guid.NewGuid(), 60);
    private static readonly LandCoverClassCode Water = new(Guid.NewGuid(), 80);
    private static readonly IReadOnlyList<LandCoverClassCode> Classes = [Trees, Bare, Water];

    private static readonly IReadOnlyList<Position> Square = [new(25, 55), new(25, 55.01), new(25.01, 55.01), new(25.01, 55)];

    private static LandCoverPixel At(double latitude, double longitude, byte code) => new(new Position(latitude, longitude), Pixel, Pixel, code);

    private static LandCoverShare ShareOf(IReadOnlyList<LandCoverShare> shares, LandCoverClassCode landCover) =>
        shares.Should().ContainSingle(share => share.Term == landCover.Term).Subject;

    [Fact]
    public void Only_pixels_whose_centre_is_inside_the_border_are_counted()
    {
        var shares = new LandCoverShares(Square, Classes);

        shares.Count(At(25.005, 55.005, 10));
        shares.Count(At(25.005, 55.0101, 10));
        shares.Count(At(24.9999, 55.005, 60));

        ShareOf(shares.Shares, Trees).PixelCount.Should().Be(1);
        ShareOf(shares.Shares, Bare).PixelCount.Should().Be(0);
    }

    // At 25° N one pixel, 1/12000° a side, is 110574 / 12000 = 9.2145 m tall and 111320 × cos 25° / 12000 = 8.4075152 m wide.
    [Fact]
    public void A_pixel_at_25_degrees_north_covers_its_own_area_there()
    {
        var shares = new LandCoverShares([new(24.99, 55), new(24.99, 55.01), new(25.01, 55.01), new(25.01, 55)], Classes);

        shares.Count(At(25, 55.005, 10));

        ShareOf(shares.Shares, Trees).AreaHectares.Should().BeApproximately(9.2145 * 8.4075152377 / 10000, 1e-12);
    }

    [Fact]
    public void A_share_is_the_share_of_the_area_so_a_pixel_at_60_degrees_counts_half_one_at_the_equator()
    {
        var shares = new LandCoverShares([new(-1, 0), new(-1, 1), new(61, 1), new(61, 0)], Classes);

        shares.Count(At(0, 0.5, 10));
        shares.Count(At(60, 0.5, 60));

        ShareOf(shares.Shares, Trees).SharePercent.Should().BeApproximately(200.0 / 3, 1e-9);
        ShareOf(shares.Shares, Bare).SharePercent.Should().BeApproximately(100.0 / 3, 1e-9);
    }

    [Fact]
    public void A_code_no_class_holds_is_left_out_so_the_shares_add_up_to_100()
    {
        var shares = new LandCoverShares(Square, Classes);

        shares.Count(At(25.004, 55.005, 10));
        shares.Count(At(25.005, 55.005, 0));
        shares.Count(At(25.006, 55.005, 0));
        shares.Count(At(25.007, 55.005, 200));

        ShareOf(shares.Shares, Trees).SharePercent.Should().Be(100);
        ShareOf(shares.Shares, Trees).PixelCount.Should().Be(1);
        shares.Shares.Sum(share => share.PixelCount).Should().Be(1);
    }

    [Fact]
    public void Every_class_has_a_share_and_one_with_no_pixel_has_0()
    {
        var shares = new LandCoverShares(Square, Classes);

        shares.Count(At(25.005, 55.005, 60));

        shares.Shares.Select(share => share.Term).Should().Equal(Trees.Term, Bare.Term, Water.Term);
        ShareOf(shares.Shares, Water).Should().Be(new LandCoverShare(Water.Term, 0, 0, 0));
    }

    [Fact]
    public void No_pixel_inside_the_border_gives_no_shares()
    {
        var shares = new LandCoverShares(Square, Classes);

        shares.Count(At(26, 55.005, 60));
        shares.Count(At(25.005, 55.005, 0));

        shares.Shares.Should().BeEmpty();
    }
}
