using System.Text.Json;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class AModelIsDescribedAndThingsAreFoundOnTheTestSeedTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public AModelIsDescribedAndThingsAreFoundOnTheTestSeedTests(TheEngine engine) => _engine = engine;

    private async Task<(Guid Id, string Name)> TheTestSeedAsAModelAsync()
    {
        var name = $"Catchments {Guid.NewGuid():N}"[..20];
        return (await _engine.CreateModelFromAsync(TestSeeds.Catchments, name), name);
    }

    private static IEnumerable<string> Names(JsonElement listed, string field = "name") =>
        listed.EnumerateArray().Select(entry => entry.GetProperty(field).GetString()!);

    [FactAgainstTheEngine]
    public async Task The_test_seed_is_described_by_its_own_kinds_and_the_engines_first_model_is_not()
    {
        var seed = await TheTestSeedAsAModelAsync();
        var first = await _engine.TheModelAsync();
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var described = await server.CallAsync("describe_model", new() { ["model"] = seed.Id.ToString() });
        var inTheFirst = await server.CallAsync("describe_model", new() { ["model"] = first.Id.ToString() });

        described.IsError.Should().BeFalse(described.Text);
        Names(described.Structured.GetProperty("kinds"), "Name").Should().Contain(["Catchment", "Reservoir", "Spring"]);
        inTheFirst.IsError.Should().BeFalse(inTheFirst.Text);
        Names(inTheFirst.Structured.GetProperty("kinds"), "Name").Should().NotContain("Catchment");
    }

    [FactAgainstTheEngine]
    public async Task Things_are_found_by_part_of_a_name_in_the_model_asked_and_not_in_another()
    {
        var seed = await TheTestSeedAsAModelAsync();
        var first = await _engine.TheModelAsync();
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var inTheSeed = await server.CallAsync("find_things", new() { ["name_contains"] = "SLOPE", ["model"] = seed.Name });
        var inTheFirst = await server.CallAsync("find_things", new() { ["name_contains"] = "SLOPE", ["model"] = first.Id.ToString() });

        inTheSeed.IsError.Should().BeFalse(inTheSeed.Text);
        Names(inTheSeed.Structured.GetProperty("things")).Should().BeEquivalentTo(["Ålvik north slope", "Ålvik south slope"]);
        inTheFirst.IsError.Should().BeFalse(inTheFirst.Text);
        inTheFirst.Structured.GetProperty("things").GetArrayLength().Should().Be(0);
    }

    [FactAgainstTheEngine]
    public async Task A_name_written_in_another_script_is_found_whole_by_part_of_it()
    {
        var seed = await TheTestSeedAsAModelAsync();
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var found = await server.CallAsync("find_things", new()
        {
            ["name_contains"] = "النبع", ["model"] = seed.Id.ToString(),
        });

        found.IsError.Should().BeFalse(found.Text);
        Names(found.Structured.GetProperty("things")).Should().BeEquivalentTo(["وادي النبع"]);
    }
}
