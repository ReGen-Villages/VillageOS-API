using FluentAssertions;
using vos.Service.LandAllocation.Services;
using Xunit;

namespace vos.Service.LandAllocation.Tests;

// The arithmetic on its own, with no model behind it: shares into areas, and the two footprints the
// balances read. Every expected value is worked out here rather than copied from the implementation.
public class LandAllocationCalculatorTests
{
    private static AllocatedCategory Category(string name, double share, bool built = false, bool productive = false) =>
        new(name, share, built, productive);

    [Fact]
    public void Shares_totalling_one_hundred_divide_the_parcel_between_them()
    {
        // LAND_INTAKE.md's worked example: 24 ha split six ways.
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(24.0,
        [
            Category("residential", 22, built: true),
            Category("food-and-agriculture", 34, productive: true),
            Category("green-water-and-restoration", 20),
            Category("commercial-and-retail", 8, built: true),
            Category("community-education-and-health", 9),
            Category("mobility-and-infrastructure", 7, built: true),
        ]));

        result.AreaByCategory["residential"].Should().BeApproximately(5.28, 1e-9);
        result.AreaByCategory["food-and-agriculture"].Should().BeApproximately(8.16, 1e-9);
        result.AreaByCategory.Values.Sum().Should().BeApproximately(24.0, 1e-9);
        result.BuiltFootprintHectares.Should().BeApproximately(5.28 + 1.92 + 1.68, 1e-9);
        result.ProductiveFootprintHectares.Should().BeApproximately(8.16, 1e-9);
    }

    [Fact]
    public void Shares_that_do_not_total_one_hundred_are_normalised_over_the_whole_parcel()
    {
        // 30 and 10 describe the parcel three-to-one however they were written down.
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(20.0,
        [Category("residential", 30, built: true), Category("food-and-agriculture", 10, productive: true)]));

        result.AreaByCategory["residential"].Should().BeApproximately(15.0, 1e-9);
        result.AreaByCategory["food-and-agriculture"].Should().BeApproximately(5.0, 1e-9);
        result.AreaByCategory.Values.Sum().Should().BeApproximately(20.0, 1e-9);
        result.NormalisedSharePct["residential"].Should().BeApproximately(75.0, 1e-9);
        result.NormalisedSharePct["food-and-agriculture"].Should().BeApproximately(25.0, 1e-9);
    }

    [Fact]
    public void A_category_marked_for_both_footprints_counts_into_both()
    {
        // A roofed growing area is hard surface the rain runs off and land that grows food. The
        // footprints were never a partition of the parcel; the per-category areas are.
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(10.0,
        [Category("glasshouse", 100, built: true, productive: true)]));

        result.BuiltFootprintHectares.Should().BeApproximately(10.0, 1e-9);
        result.ProductiveFootprintHectares.Should().BeApproximately(10.0, 1e-9);
        result.AreaByCategory.Values.Sum().Should().BeApproximately(10.0, 1e-9);
    }

    [Fact]
    public void A_category_marked_for_neither_still_takes_its_area()
    {
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(10.0,
        [Category("residential", 50, built: true), Category("green-water-and-restoration", 50)]));

        result.AreaByCategory["green-water-and-restoration"].Should().BeApproximately(5.0, 1e-9);
        result.BuiltFootprintHectares.Should().BeApproximately(5.0, 1e-9);
        result.ProductiveFootprintHectares.Should().Be(0);
    }

    [Fact]
    public void An_empty_selection_allocates_nothing_and_does_not_divide_by_zero()
    {
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(24.0, []));

        result.AreaByCategory.Should().BeEmpty();
        result.BuiltFootprintHectares.Should().Be(0);
        result.ProductiveFootprintHectares.Should().Be(0);
    }

    [Fact]
    public void Shares_totalling_zero_allocate_nothing_rather_than_a_not_a_number()
    {
        // Every category selected and every share left at zero. Dividing by the total would put a
        // not-a-number on the study, which reads downstream as a figure rather than as an absence.
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(24.0,
        [Category("residential", 0, built: true), Category("food-and-agriculture", 0, productive: true)]));

        result.AreaByCategory.Values.Should().OnlyContain(area => area == 0);
        result.NormalisedSharePct.Values.Should().OnlyContain(share => share == 0);
        result.BuiltFootprintHectares.Should().Be(0);
    }

    [Fact]
    public void A_parcel_of_no_area_allocates_no_area_but_still_normalises_the_split()
    {
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(0.0,
        [Category("residential", 75, built: true), Category("food-and-agriculture", 25, productive: true)]));

        result.AreaByCategory.Values.Should().OnlyContain(area => area == 0);
        result.NormalisedSharePct["residential"].Should().BeApproximately(75.0, 1e-9);
    }

    [Fact]
    public void A_negative_share_is_refused_rather_than_taking_area_from_another_category()
    {
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
            LandAllocationCalculator.Compute(new LandAllocationInputs(24.0,
                [Category("residential", -10, built: true), Category("food-and-agriculture", 110, productive: true)])));

        refusal.Message.Should().Contain("residential");
    }

    [Fact]
    public void Nothing_is_rounded()
    {
        var result = LandAllocationCalculator.Compute(new LandAllocationInputs(1.0,
        [Category("a", 1), Category("b", 2)]));

        result.AreaByCategory["a"].Should().BeApproximately(1.0 / 3.0, 1e-15);
    }
}
