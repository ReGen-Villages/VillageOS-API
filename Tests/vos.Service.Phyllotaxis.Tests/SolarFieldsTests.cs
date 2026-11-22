using FluentAssertions;
using vos.Service.Phyllotaxis.Placement;
using Xunit;

namespace vos.Service.Phyllotaxis.Tests;

// 21 by 21 cells of 30 m. Unless a test says otherwise, the sun share rises from nothing in the west column to all of it in the east one.
public class SolarFieldsTests
{
    private static readonly SolarFieldAssumptions Assumptions = DemoAssumptions.SolarField;
    private static readonly double OneFieldRadius = Math.Sqrt(30_000 / Math.PI);

    private static TerrainGrid Field(Func<int, int, double>? slope = null, Func<int, int, Ground>? ground = null) =>
        Grids.Build(21, 21, 30, (_, _) => 0, slope, ground: ground);

    private static SunAndShade Sunshine(TerrainGrid grid, Func<int, double> share) =>
        new(Enumerable.Range(0, grid.CellCount).Select(index => share(grid.CellAt(index).Column)).ToList(), Enumerable.Repeat(0.0, grid.CellCount).ToList());

    private static SunAndShade RisingEastward(TerrainGrid grid) => Sunshine(grid, column => column / 20.0);

    private static IReadOnlyList<SolarField> Place(TerrainGrid grid, SunAndShade sun, double squareMetres, Cell? village = null, double builtRadius = 0, IReadOnlyList<TurbinePlace>? turbines = null) =>
        SolarFields.Place(grid, sun, squareMetres, village, builtRadius, [], turbines ?? Array.Empty<TurbinePlace>(), DemoAssumptions.Clearance, Assumptions);

    [Fact]
    public void One_field_whose_window_must_lie_inside_the_grid_goes_to_the_sunniest_column_it_fits_in()
    {
        var grid = Field();

        var fields = Place(grid, RisingEastward(grid), 30_000);

        fields.Should().ContainSingle();
        fields[0].Cell.Should().Be(new Cell(17, 3));
        fields[0].Centre.Should().Be(new Point(510, 90));
        fields[0].RadiusMetres.Should().BeApproximately(OneFieldRadius, 1e-9);
        fields[0].SunShare.Should().BeApproximately(0.85, 1e-12);
    }

    [Fact]
    public void An_area_larger_than_one_field_is_split_into_fields_a_diameter_and_a_gap_apart()
    {
        var grid = Field();

        var fields = Place(grid, RisingEastward(grid), 50_000);

        fields.Select(field => field.Cell).Should().Equal(new Cell(18, 2), new Cell(18, 10));
        fields.Should().OnlyContain(field => Math.Abs(field.RadiusMetres - Math.Sqrt(25_000 / Math.PI)) < 1e-9);
    }

    [Fact]
    public void A_patch_steeper_than_eight_per_cent_on_average_is_passed_over()
    {
        var grid = Field(slope: (column, _) => column >= 11 ? 9 : 0);

        var patches = SolarFields.BestPatches(grid, RisingEastward(grid), 1, 3, 0, [], Assumptions);

        patches.Select(patch => patch.Cell).Should().Equal(new Cell(12, 3));
        patches[0].SlopePercent.Should().BeApproximately(9.0 * 23 / 29, 1e-9);
    }

    [Fact]
    public void A_patch_centred_on_ground_kept_clear_is_passed_over()
    {
        var grid = Field(ground: (column, row) => column == 17 && row == 3 ? Ground.ProtectedZone : Ground.Buildable);

        var patches = SolarFields.BestPatches(grid, RisingEastward(grid), 1, 3, 0, [], Assumptions);

        patches.Select(patch => patch.Cell).Should().Equal(new Cell(17, 4));
    }

    [Fact]
    public void A_field_keeps_clear_of_a_turbine_by_the_clearance_and_its_own_radius()
    {
        var grid = Field();
        var turbine = new TurbinePlace(new Cell(17, 10), new Point(510, 300), 0);

        var fields = Place(grid, RisingEastward(grid), 30_000, turbines: [turbine]);

        fields.Select(field => field.Cell).Should().Equal(new Cell(12, 3));
    }

    [Fact]
    public void When_no_field_fits_clear_of_the_village_the_village_clearance_is_narrowed_to_the_next_factor()
    {
        var grid = Field();

        var fields = Place(grid, Sunshine(grid, _ => 1), 30_000, new Cell(10, 10), 120);

        fields.Select(field => field.Cell).Should().Equal(new Cell(3, 3));
    }

    [Fact]
    public void No_agrivoltaic_area_places_no_field()
    {
        var grid = Field();

        Place(grid, RisingEastward(grid), 0).Should().BeEmpty();
    }

    [Fact]
    public void A_grid_too_small_for_any_field_places_none()
    {
        var grid = Grids.Build(5, 5, 30, (_, _) => 0);

        Place(grid, RisingEastward(grid), 30_000).Should().BeEmpty();
    }
}
