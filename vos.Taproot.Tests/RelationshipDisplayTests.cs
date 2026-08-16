using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

/// <summary>How a relationship reads on screen, against the payload the platform actually serves.
///
/// <c>GET /api/relationships</c> sends the four identifiers and sends a name only for an edge given
/// one of its own — which nothing in the platform currently does. Everything else is named by the
/// three endpoints, each of which resolves through the Thing name map the handlers already build.</summary>
public class RelationshipDisplayTests
{
    private readonly Mock<MyceliumClient> _mycelium = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private static readonly Guid Kitchen = Guid.NewGuid();
    private static readonly Guid Contains = Guid.NewGuid();
    private static readonly Guid Sink = Guid.NewGuid();

    private static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    private static string Things() => $@"[
        {{""Id"":""{Kitchen}"",""Name"":""Kitchen""}},
        {{""Id"":""{Contains}"",""Name"":""contains""}},
        {{""Id"":""{Sink}"",""Name"":""Sink""}}
    ]";

    // The shape the read route serves: four identifiers, and no Name unless the edge was given one.
    private static string Edge(string? ownName = null) => $@"[
        {{""Id"":""{Guid.NewGuid()}"",
          ""SubjectId"":""{Kitchen}"",""PredicateId"":""{Contains}"",""TargetId"":""{Sink}""
          {(ownName is null ? "" : $@",""Name"":""{ownName}""")}}}
    ]";

    private void Serving(string relationships)
    {
        _mycelium.Setup(m => m.GetAllThingsAsync()).ReturnsAsync(Parse(Things()));
        _mycelium.Setup(m => m.GetAllRelationshipsAsync()).ReturnsAsync(Parse(relationships));
        _mycelium.Setup(m => m.GetThingAsync(Kitchen))
            .ReturnsAsync(Parse($@"{{""Id"":""{Kitchen}"",""Name"":""Kitchen""}}"));
        _mycelium.Setup(m => m.GetAllPropertiesAsync(It.IsAny<string>())).ReturnsAsync(Parse("[]"));
    }

    private async Task FindAsync(string arg) =>
        await new FindCommandHandler(arg, _writer, _mycelium.Object).ExecuteAsync();

    private async Task ListAsync(string arg) =>
        await new ListCommandHandler(arg, _writer, _mycelium.Object).ExecuteAsync();

    [Fact]
    public async Task Find_puts_the_predicate_between_the_arrows()
    {
        Serving(Edge());

        await FindAsync($"relationships {Kitchen}");

        Assert.Contains("--[contains]-->", _writer.ToString());
    }

    [Fact]
    public async Task Find_shows_a_name_the_edge_was_given_alongside_the_predicate()
    {
        Serving(Edge("The one under the window"));

        await FindAsync($"relationships {Kitchen}");

        var output = _writer.ToString();
        Assert.Contains("--[contains]-->", output);
        Assert.Contains("The one under the window", output);
    }

    [Fact]
    public async Task List_heads_an_unnamed_edge_with_its_endpoints()
    {
        Serving(Edge());

        await ListAsync("relations");

        Assert.Contains("Kitchen contains Sink", _writer.ToString());
    }

    [Fact]
    public async Task List_heads_an_edge_with_the_name_it_was_given()
    {
        Serving(Edge("The one under the window"));

        await ListAsync("relations");

        Assert.Contains("The one under the window", _writer.ToString());
    }
}
