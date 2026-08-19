using FluentAssertions;
using vos.Service.FoodBalance.Services;
using vos.Service.LandAllocation.Services;
using vos.Service.RainwaterHarvest.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.SiteAnalysis.Tests;

// The seam between the three services that compute a site analysis, which no one of them can pin on its
// own. Land allocation works out the footprints; the rainwater harvest and the food balance read them off
// the same study and write their own results back onto it.
//
// The three were specified together and built separately, so this is where what each one assumed about the
// other two is checked against what was actually built.
public class SiteAnalysisChainTests
{
    // The footprint is one property name rather than a pair on either side that happen to agree, so the
    // value a balance reads is the value land allocation wrote and there is no step between them that could
    // convert it. The unit is in the name, which is what makes a move from hectares to square metres a
    // rename both sides have to follow rather than a silent factor of ten thousand.
    [Fact]
    public void Each_balance_reads_the_footprints_under_the_names_land_allocation_writes_them()
    {
        RainwaterHarvestReactiveHandler.InputProperties.Should().Contain(new[]
        {
            LandAllocationReactiveHandler.BuiltFootprintOutput,
            LandAllocationReactiveHandler.ProductiveFootprintOutput,
        });

        FoodBalanceReactiveHandler.InputProperties.Should()
            .Contain(LandAllocationReactiveHandler.ProductiveFootprintOutput);
    }

    // And the arithmetic behind each balance takes the figure under that same name, so the unit cannot be
    // reinterpreted between the property the handler read and the calculation it fed.
    [Fact]
    public void Each_balances_arithmetic_takes_the_footprint_under_that_same_name()
    {
        TakenBy<RainwaterHarvestInputs>().Should().Contain(new[]
        {
            LandAllocationReactiveHandler.BuiltFootprintOutput,
            LandAllocationReactiveHandler.ProductiveFootprintOutput,
        });

        TakenBy<FoodBalanceInputs>().Should().Contain(LandAllocationReactiveHandler.ProductiveFootprintOutput);
    }

    // A footprint moving wakes both balances, each writes its results onto the study it was woken by, and
    // there the wave stops: nothing in the chain reads what a balance writes. That is why a planner nudging
    // a programme share settles in one round rather than running up against the model's round limit, and it
    // is the half of the arrangement no single service can state.
    [Fact]
    public void Nothing_in_the_chain_is_woken_by_what_a_balance_writes()
    {
        var balanceResults = DeclaredOutputs.Of<RainwaterHarvestReactiveHandler>()
            .Concat(DeclaredOutputs.Of<FoodBalanceReactiveHandler>())
            .ToArray();

        balanceResults.Should().NotBeEmpty("an empty set would pass the check below without checking it");

        foreach (var woken in new[]
                 {
                     LandAllocationReactiveHandler.InputProperties,
                     RainwaterHarvestReactiveHandler.InputProperties,
                     FoodBalanceReactiveHandler.InputProperties,
                 })
            woken.Should().NotIntersectWith(balanceResults);
    }

    // A calculator takes its figures as a record and the handler passes each one under the name of the
    // property it was read from, so the parameter names are those property names with an upper initial.
    private static IEnumerable<string> TakenBy<TInputs>() =>
        typeof(TInputs).GetConstructors().Single().GetParameters()
            .Select(parameter => char.ToLowerInvariant(parameter.Name![0]) + parameter.Name[1..]);
}
