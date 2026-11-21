using FluentAssertions;
using vos.Service.Shared.Geometry;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.Service.Shared.Tests.Geometry;

// The WGS84 figures are Karney's geodesic polygon areas on the WGS84 ellipsoid, from geographiclib 2.0.
public class BorderAreaTests
{
    private const double DegreeAtEquatorMetres = 111194.9;

    public static readonly IReadOnlyList<(double Latitude, double Longitude)> ReferenceBorder =
    [
        (25.1548913568011, 55.55616063595723), (25.15129015552567, 55.54932911385871), (25.14978772652261, 55.5468216600034),
        (25.14886667062588, 55.54735722283466), (25.15562986578318, 55.5691377022649), (25.15707226778431, 55.56848828530388),
        (25.15754361591318, 55.56296846877041), (25.15637327045562, 55.56259408448088), (25.15624859750918, 55.55932941382773),
        (25.1551934630042, 55.55941873916951), (25.15471781966161, 55.55817561038458), (25.15484810477037, 55.55807414688074),
        (25.15462140660673, 55.55752719634192), (25.15409802690795, 55.55771982371159), (25.15378546087098, 55.55684163276631),
    ];

    private static IReadOnlyList<(double Latitude, double Longitude)> Box(double latitude, double side) =>
        [(latitude, 0), (latitude, side), (latitude + side, side), (latitude + side, 0)];

    [Fact]
    public void The_reference_border_measures_what_the_platform_has_always_measured_on_the_mean_sphere()
    {
        BorderArea.Hectares(ReferenceBorder, AreaMethod.MeanSphere).Should().BeApproximately(62.484565, 1e-6);
    }

    [Fact]
    public void The_reference_border_measures_its_area_on_the_wgs84_ellipsoid()
    {
        BorderArea.Hectares(ReferenceBorder, AreaMethod.Wgs84Ellipsoid).Should().BeApproximately(62.347761, 62.347761 * 1e-5);
    }

    [Theory]
    [InlineData(0, 0.01, 123.090720)]
    [InlineData(45, 0.01, 87.616345)]
    [InlineData(60, 0.01, 62.158724)]
    [InlineData(-60.01, 0.01, 62.158724)]
    [InlineData(0, 1.0, 1230877.836147)]
    public void A_box_measures_the_wgs84_ellipsoid_area_at_any_latitude_and_size(double latitude, double side, double karneyHectares)
    {
        BorderArea.Hectares(Box(latitude, side), AreaMethod.Wgs84Ellipsoid).Should().BeApproximately(karneyHectares, karneyHectares * 1e-5);
    }

    [Fact]
    public void A_box_at_the_equator_measures_its_sides_multiplied_on_the_mean_sphere()
    {
        var sideMetres = 0.01 * DegreeAtEquatorMetres;

        BorderArea.Hectares(Box(latitude: 0, side: 0.01), AreaMethod.MeanSphere)
            .Should().BeApproximately(sideMetres * sideMetres / 10_000, 0.1);
    }

    [Theory]
    [InlineData(AreaMethod.MeanSphere)]
    [InlineData(AreaMethod.Wgs84Ellipsoid)]
    public void The_same_box_encloses_half_as_much_at_sixty_degrees_north(AreaMethod method)
    {
        var equator = BorderArea.Hectares(Box(latitude: 0, side: 0.01), method);

        BorderArea.Hectares(Box(latitude: 60, side: 0.01), method).Should().BeApproximately(equator * 0.5, equator * 0.01);
    }

    [Theory]
    [InlineData(AreaMethod.MeanSphere)]
    [InlineData(AreaMethod.Wgs84Ellipsoid)]
    public void Which_way_round_the_corners_were_drawn_does_not_change_the_measure(AreaMethod method)
    {
        var clockwise = Box(latitude: 39.5, side: 0.01);

        BorderArea.Hectares([.. clockwise.Reverse()], method).Should().BeApproximately(BorderArea.Hectares(clockwise, method), 1e-9);
    }

    [Theory]
    [InlineData(AreaMethod.MeanSphere)]
    [InlineData(AreaMethod.Wgs84Ellipsoid)]
    public void A_ring_that_repeats_its_first_corner_measures_the_same(AreaMethod method)
    {
        var open = Box(latitude: 39.5, side: 0.01);

        BorderArea.Hectares([.. open, open[0]], method).Should().BeApproximately(BorderArea.Hectares(open, method), 1e-9);
    }

    [Theory]
    [InlineData(AreaMethod.MeanSphere)]
    [InlineData(AreaMethod.Wgs84Ellipsoid)]
    public void A_concave_border_measures_less_than_the_box_around_it(AreaMethod method)
    {
        var notched = new List<(double Latitude, double Longitude)>
            { (39.5, 0), (39.5, 0.01), (39.51, 0.01), (39.505, 0.005), (39.51, 0) };

        var threeQuarters = BorderArea.Hectares(Box(39.5, 0.01), method) * 0.75;

        BorderArea.Hectares(notched, method).Should().BeApproximately(threeQuarters, threeQuarters * 1e-3);
    }

    [Theory]
    [InlineData(AreaMethod.MeanSphere)]
    [InlineData(AreaMethod.Wgs84Ellipsoid)]
    public void A_border_of_fewer_than_three_corners_encloses_nothing(AreaMethod method)
    {
        BorderArea.Hectares([(39.5, 0), (39.5, 0.01)], method).Should().Be(0);
    }

    [Fact]
    public void A_method_no_formula_measures_with_is_refused()
    {
        var measuring = () => BorderArea.Hectares(Box(latitude: 39.5, side: 0.01), (AreaMethod)99);

        measuring.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("method");
    }

    [Fact]
    public void Each_method_the_platform_implements_is_bound_by_its_own_role()
    {
        AreaMethods.Implemented.Should().BeEquivalentTo(new[]
        {
            (LandIntakeRoles.MeanSphereAreaMethod, AreaMethod.MeanSphere),
            (LandIntakeRoles.Wgs84EllipsoidAreaMethod, AreaMethod.Wgs84Ellipsoid),
        });
    }
}
