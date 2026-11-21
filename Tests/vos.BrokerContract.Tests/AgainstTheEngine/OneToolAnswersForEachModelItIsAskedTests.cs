using System.ComponentModel;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Server;
using vos.Hypha.Registry;
using vos.PlatformClient;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public class OneToolAnswersForEachModelItIsAskedTests : IClassFixture<TheEngine>
{
    private const string NotFound = "none";

    private readonly TheEngine _engine;

    public OneToolAnswersForEachModelItIsAskedTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public async Task A_tool_answers_from_the_model_it_is_given_by_identifier_or_name_and_from_its_own_when_given_none()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var inTheFirst = $"Upper spring {run}";
        var inTheSecond = $"Lower spring {run}";
        var secondName = $"Second catchment {run}";
        await _engine.DeclareAsync(inTheFirst);
        var seed = ASeedHolding(inTheSecond);
        var second = await _engine.CreateModelFromAsync(seed, secondName);
        File.Delete(seed);

        await using var server = await HyphaAgainstTheEngine.StartAsync(_engine, "viewer", typeof(ThingNamed));

        (await FindAsync(server, inTheFirst)).Should().Be(inTheFirst);
        (await FindAsync(server, inTheSecond)).Should().Be(NotFound, "the server's own model holds no such Thing");
        (await FindAsync(server, inTheSecond, second.ToString())).Should().Be(inTheSecond);
        (await FindAsync(server, inTheSecond, secondName)).Should().Be(inTheSecond);
        (await FindAsync(server, inTheFirst, second.ToString())).Should().Be(NotFound, "the second model holds no such Thing");
        (await FindAsync(server, inTheFirst)).Should().Be(inTheFirst, "asking another model leaves the server's own as it was");
    }

    private static async Task<string> FindAsync(HyphaAgainstTheEngine server, string name, string? model = null)
    {
        var arguments = new Dictionary<string, object?> { ["name"] = name };
        if (model is not null) arguments["model"] = model;

        var answer = await server.CallAsync("thing_named", arguments);
        answer.IsError.Should().BeFalse(answer.Text);
        return answer.Text;
    }

    private static string ASeedHolding(string thingName)
    {
        var file = Path.Combine(Path.GetTempPath(), $"second-model-{Guid.NewGuid():N}.seed.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            Id = Guid.NewGuid(),
            Name = "a template",
            Things = new[] { thingName, "is", "binds" }.Select(name => new { Id = Guid.NewGuid(), Name = name, Properties = new { } }),
            Relationships = Array.Empty<object>(),
        }));
        return file;
    }

    // This Story adds no tool that reads a model, so the test brings its own.
    [McpServerToolType]
    public sealed class ThingNamed(ModelClients clients)
    {
        [McpServerTool(Name = "thing_named", ReadOnly = true)]
        [Description("Answers the name of the one Thing of that name in the model asked, or none.")]
        [RequiresPolicy(MyceliumPolicy.ReadModel)]
        public async Task<string> FindAsync(string name, string? model = null)
        {
            var found = await (await clients.ForAsync(model)).GetThingByNameAsync(name);
            return found.Thing is { } thing ? thing.GetProperty("Name").GetString()! : NotFound;
        }
    }
}
