using FluentAssertions;
using vos.Service.FoodBalance.Services;
using Xunit;

namespace vos.Service.FoodBalance.Tests;

// The arithmetic on its own, with no model behind it. Every expected value is worked out here rather
// than copied from the implementation.
public class FoodBalanceCalculatorTests
{
    [Fact]
    public void The_productive_land_and_the_yield_give_the_people_fed_and_their_share_of_the_population()
    {
        // LAND_INTAKE.md's worked example: 8.16 ha of the 24 goes to food and agriculture, at the
        // 2.5 people per hectare the shared study archetype declares, against 320 residents.
        var result = FoodBalanceCalculator.Compute(new FoodBalanceInputs(8.16, 2.5, 320));

        result.PeopleFed.Should().BeApproximately(20.4, 1e-9);
        result.PctOfPopulationFed.Should().BeApproximately(6.375, 1e-9);
    }

    [Fact]
    public void Nobody_to_feed_is_a_share_of_nothing_rather_than_a_division()
    {
        var result = FoodBalanceCalculator.Compute(new FoodBalanceInputs(8.16, 2.5, 0));

        result.PeopleFed.Should().BeApproximately(20.4, 1e-9);
        result.PctOfPopulationFed.Should().Be(0);
    }

    [Fact]
    public void No_productive_land_feeds_nobody()
    {
        var result = FoodBalanceCalculator.Compute(new FoodBalanceInputs(0, 2.5, 320));

        result.PeopleFed.Should().Be(0);
        result.PctOfPopulationFed.Should().Be(0);
    }

    [Fact]
    public void Neither_output_is_rounded_to_the_whole_person_the_figure_stands_for()
    {
        // 1.3 ha at 2.5 feeds 3.25 people out of 3. Rounded to 3 the site would read as feeding exactly
        // its population, which is the disagreement rounding here would introduce.
        var result = FoodBalanceCalculator.Compute(new FoodBalanceInputs(1.3, 2.5, 3));

        result.PeopleFed.Should().BeApproximately(3.25, 1e-9);
        result.PctOfPopulationFed.Should().BeApproximately(108.33333333, 1e-8);
    }
}
