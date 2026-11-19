using System.Text.Json;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class AThingAndOnePropertyAreReadOnTheTestSeedTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public AThingAndOnePropertyAreReadOnTheTestSeedTests(TheEngine engine) => _engine = engine;

    private async Task<string> TheTestSeedAsAModelAsync() =>
        (await _engine.CreateModelFromAsync(TestSeeds.Catchments, $"Catchments {Guid.NewGuid():N}"[..20])).ToString();

    [FactAgainstTheEngine]
    public async Task A_Thing_of_the_test_seed_is_read_with_what_it_inherits_from_its_properties_and_its_states()
    {
        var seed = await TheTestSeedAsAModelAsync();
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var thing = await server.CallAsync("get_thing", new() { ["thing"] = "Ålvik south slope", ["model"] = seed });

        thing.IsError.Should().BeFalse(thing.Text);
        thing.Structured.GetProperty("name").GetString().Should().Be("Ålvik south slope");
        thing.Structured.GetProperty("inheritsFrom").EnumerateArray().Select(source => source.GetProperty("name").GetString())
            .Should().Contain("Catchment");
        thing.Structured.GetProperty("properties").EnumerateArray()
            .Where(property => property.GetProperty("isInherited").GetBoolean())
            .Should().OnlyContain(property => property.GetProperty("inheritedFromName").ValueKind == JsonValueKind.String,
                "every inherited property is named from the tree the platform answers");
        thing.Structured.GetProperty("currentStates").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [FactAgainstTheEngine]
    public async Task A_Thing_of_one_model_is_not_answered_from_another()
    {
        await TheTestSeedAsAModelAsync();
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var refused = await server.CallAsync("get_thing", new() { ["thing"] = "Ålvik south slope" });

        refused.IsError.Should().BeTrue();
        refused.Text.Should().Contain("No thing found with name 'Ålvik south slope'.");
    }

    [FactAgainstTheEngine]
    public async Task An_inherited_property_is_read_by_its_qualified_path()
    {
        var seed = await TheTestSeedAsAModelAsync();
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var plain = await server.CallAsync("read_property", new() { ["thing"] = "Ålvik south slope", ["path"] = "areaHectares", ["model"] = seed });
        var qualified = await server.CallAsync("read_property", new() { ["thing"] = "Ålvik south slope", ["path"] = "Catchment.areaHectares", ["model"] = seed });

        plain.IsError.Should().BeFalse(plain.Text);
        qualified.IsError.Should().BeFalse(qualified.Text);
        qualified.Structured.GetProperty("value").GetRawText().Should().Be(plain.Structured.GetProperty("value").GetRawText());
    }

    [FactAgainstTheEngine]
    public async Task A_property_whose_name_holds_a_slash_is_read_back()
    {
        var name = $"Gauged spring {Guid.NewGuid():N}";
        await _engine.DeclareAsync(name, properties: new Dictionary<string, object?> { ["flow m3/h"] = 4.5 });
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var read = await server.CallAsync("read_property", new() { ["thing"] = name, ["path"] = "flow m3/h" });

        read.IsError.Should().BeFalse(read.Text);
        read.Structured.GetProperty("value").GetDouble().Should().Be(4.5);
    }

    [FactAgainstTheEngine]
    public async Task A_path_of_only_dots_is_refused_before_the_engine_is_asked()
    {
        var name = $"Dry spring {Guid.NewGuid():N}";
        await _engine.DeclareAsync(name, properties: new Dictionary<string, object?> { ["flow"] = 1.0 });
        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer");

        var refused = await server.CallAsync("read_property", new() { ["thing"] = name, ["path"] = ".." });
        var stillThere = await server.CallAsync("get_thing", new() { ["thing"] = name });

        refused.IsError.Should().BeTrue();
        refused.Text.Should().Contain("is not a path to one of the platform's routes");
        stillThere.IsError.Should().BeFalse(stillThere.Text);
    }
}
