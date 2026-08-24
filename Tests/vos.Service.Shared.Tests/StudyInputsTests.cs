using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

// Bug 6744: the broker answers a study's effective properties with its own under their bare name and
// every inherited one under a key qualified by the set it came from. Since the shared study archetype
// took over the assumptions, reading by bare name alone found none of them — and every handler test
// stubbed the answer with bare keys, so the lookup was only ever exercised against a shape the broker
// does not produce.
public class StudyInputsTests
{
    private const string Service = "WaterReserve";

    private static StudyInputs Reading(string json) =>
        new(JsonSerializer.Deserialize<JsonElement>(json), Service);

    [Fact]
    public void An_own_value_is_read_by_its_name()
    {
        var inputs = Reading("""{ "population": { "Value": 300 } }""");

        inputs.Number("population").Should().Be(300);
    }

    [Fact]
    public void An_inherited_value_is_read_by_the_name_it_is_declared_under()
    {
        var inputs = Reading("""{ "SiteStudy.perCapitaConsumptionM3": { "Value": 55 } }""");

        inputs.Number("perCapitaConsumptionM3").Should().Be(55);
    }

    // A study's own value overrides the archetype's, and the answer carries both.
    [Fact]
    public void An_own_value_wins_over_the_inherited_one_of_the_same_name()
    {
        var inputs = Reading("""
            { "storageCapacityM3": { "Value": 900 }, "SiteStudy.storageCapacityM3": { "Value": 100 } }
            """);

        inputs.Number("storageCapacityM3").Should().Be(900);
    }

    // Two archetypes declaring the same leaf name is a model to fix, not a value to guess at.
    [Fact]
    public void Two_inherited_values_of_the_same_name_are_refused_naming_both()
    {
        var inputs = Reading("""
            { "SiteStudy.population": { "Value": 300 }, "Settlement.population": { "Value": 900 } }
            """);

        var act = () => inputs.Number("population");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SiteStudy.population*Settlement.population*");
    }

    [Fact]
    public void A_name_the_study_does_not_carry_is_refused_by_name()
    {
        var inputs = Reading("""{ "SiteStudy.population": { "Value": 300 } }""");

        var act = () => inputs.Number("perCapitaConsumptionM3");

        act.Should().Throw<KeyNotFoundException>().WithMessage($"*{Service}*perCapitaConsumptionM3*");
    }

    [Fact]
    public void An_inherited_value_that_is_null_is_refused_as_unwritten_rather_than_absent()
    {
        var inputs = Reading("""{ "SiteStudy.daysOfSupply": { "Value": null } }""");

        var act = () => inputs.Number("daysOfSupply");

        act.Should().Throw<InvalidOperationException>().WithMessage("*no value*");
    }
}
