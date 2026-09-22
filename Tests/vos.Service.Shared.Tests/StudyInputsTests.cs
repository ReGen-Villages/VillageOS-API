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
    private const string Service = "EnergyBalance";

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

    // Bug 6826: an input a study does not carry is a figure that has not arrived rather than a fault, so a
    // handler asks what it is waiting for before it reads a single value.
    [Fact]
    public void A_name_the_study_does_not_carry_is_one_it_is_waiting_for()
    {
        var inputs = Reading("""{ "SiteStudy.population": { "Value": 300 } }""");

        inputs.WaitingFor(["population", "storageCapacityM3"]).Should().Equal("storageCapacityM3");
    }

    // A roll-up whose member type names no Thing the model holds withholds its number, which says the same
    // about the study as carrying no such property at all.
    [Fact]
    public void A_name_whose_number_is_withheld_is_waited_for_as_well()
    {
        var inputs = Reading("""{ "SiteStudy.solarPvAreaM2": { "Value": null } }""");

        inputs.WaitingFor(["solarPvAreaM2"]).Should().Equal("solarPvAreaM2");
    }

    [Fact]
    public void Nothing_is_waited_for_when_every_name_is_carried_own_or_inherited()
    {
        var inputs = Reading("""
            { "population": { "Value": 300 }, "SiteStudy.perCapitaConsumptionM3": { "Value": 55 } }
            """);

        inputs.WaitingFor(["population", "perCapitaConsumptionM3"]).Should().BeEmpty();
    }

    // The route wraps every value in an envelope, and a value answered bare is read as itself rather than
    // refused: the envelope is the route's shape, not the reader's requirement.
    [Fact]
    public void A_value_answered_without_an_envelope_is_read_as_itself()
    {
        var inputs = Reading("""{ "population": 300 }""");

        inputs.Number("population").Should().Be(300);
    }

    // Two archetypes declaring one leaf name is a model to fix, whichever question is asked of it.
    [Fact]
    public void A_name_two_archetypes_declare_is_refused_rather_than_waited_for()
    {
        var inputs = Reading("""
            { "SiteStudy.population": { "Value": 300 }, "Settlement.population": { "Value": 900 } }
            """);

        var act = () => inputs.WaitingFor(["population"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SiteStudy.population*Settlement.population*");
    }
}
