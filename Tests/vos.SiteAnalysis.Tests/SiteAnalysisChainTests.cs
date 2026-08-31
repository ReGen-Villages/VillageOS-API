using FluentAssertions;
using vos.Service.FoodBalance.Services;
using vos.Service.RainwaterHarvest.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.SiteAnalysis.Tests;

// The site analysis read across the services that compute it, rather than from inside any one of them.
//
// A third service used to sit under these two and write the footprints they read, so this file could hold
// the two sides against one constant: the reader's name was the writer's name, or the build broke. The
// model works both footprints out for itself now, so the name they have to agree on belongs to the
// analysis template, and whether a study declares it is checked where that template lives.
//
// What is left here is weaker and worth naming as such. Each figure is pinned to a literal, which catches
// a rename on one side of a service and catches the two balances swapping which footprint they read — but
// no longer proves either name is one the model declares. Only the last test below is a claim no single
// service could make.
public class SiteAnalysisChainTests
{
    // The figure a handler read and the figure its arithmetic takes are one name, so no step between them
    // can reinterpret it. The unit is in the name, which is what makes a move from hectares to square
    // metres a rename both sides have to follow rather than a silent factor of ten thousand.
    [Theory]
    [InlineData("builtFootprintHectares")]
    [InlineData("rainfallMillimetresPerYear")]
    [InlineData("runoffCoefficient")]
    public void The_harvest_takes_each_figure_it_reads_under_the_name_it_read_it_by(string figure)
    {
        RainwaterHarvestReactiveHandler.InputProperties.Should().Contain(figure);
        TakenBy<RainwaterHarvestInputs>().Should().Contain(figure);
    }

    [Theory]
    [InlineData("productiveFootprintHectares")]
    [InlineData("peopleFedPerHectarePerYear")]
    public void The_food_balance_takes_each_figure_it_reads_under_the_name_it_read_it_by(string figure)
    {
        FoodBalanceReactiveHandler.InputProperties.Should().Contain(figure);
        TakenBy<FoodBalanceInputs>().Should().Contain(figure);
    }

    // A footprint moving wakes both balances, the harvest writes its results onto the study it was woken
    // by, and nothing is woken after that, because nothing in the chain reads what a balance writes. That
    // is why a planner moving a programme share settles in one round rather than running up against the
    // model's round limit, and it is the half of the arrangement no single service can state.
    //
    // The food balance asserts nothing at all now — every one of its figures is a formula on the study —
    // so what is left to check is the harvest. Its per-demand results are named by the model rather than
    // declared here, so this covers the one it names itself; the handler refuses a demand writing onto
    // anything it wakes on, which is the same guarantee for the rest.
    [Fact]
    public void Nothing_in_the_chain_is_woken_by_what_a_balance_writes()
    {
        var balanceResults = DeclaredOutputs.Of<RainwaterHarvestReactiveHandler>();

        foreach (var woken in new[]
                 {
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
