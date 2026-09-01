using FluentAssertions;
using vos.Service.EnergyBalance.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.EnergyBalance.Tests;

// What the one service the site analysis still dispatches reads and writes.
//
// These two claims were held across the analysis's services while there were several, in a test project
// of their own. The model works every figure out for itself now except the energy verdict — a boolean
// where a formula yields a number — so there is one service left and no pair to hold against each other.
// The claims are still worth making of it alone.
public class EnergyBalancePortsTests
{
    // The figure the handler reads and the figure its arithmetic takes are one name, so no step between
    // them can reinterpret it. The unit is in the name, which is what makes a move from megawatt-hours to
    // kilowatt-hours a rename both sides have to follow rather than a silent factor of a thousand.
    [Theory]
    [InlineData("solarPvAreaM2")]
    [InlineData("solarResourceKwhPerM2PerYear")]
    [InlineData("moduleEfficiency")]
    [InlineData("performanceRatio")]
    [InlineData("annualConsumptionMwhPerYear")]
    public void It_takes_each_figure_it_reads_under_the_name_it_read_it_by(string figure)
    {
        EnergyBalanceReactiveHandler.InputProperties.Should().Contain(figure);
        TakenBy<EnergyBalanceInputs>().Should().Contain(figure);
    }

    // A figure moving wakes the handler, which writes its verdict onto the study it was woken by, and
    // nothing is woken after that. That is why a planner moving a programme share settles in one round
    // rather than running up against the model's round limit.
    [Fact]
    public void It_is_not_woken_by_what_it_writes()
    {
        EnergyBalanceReactiveHandler.InputProperties.Should()
            .NotIntersectWith(DeclaredOutputs.Of<EnergyBalanceReactiveHandler>());
    }

    // A calculator takes its figures as a record and the handler passes each one under the name of the
    // property it was read from, so the parameter names are those property names with an upper initial.
    private static IEnumerable<string> TakenBy<TInputs>() =>
        typeof(TInputs).GetConstructors().Single().GetParameters()
            .Select(parameter => char.ToLowerInvariant(parameter.Name![0]) + parameter.Name[1..]);
}
