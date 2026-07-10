using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

// Covers `retype` (US #5860): swap ONE of a Thing's is-edges, preserving multiple inheritance.
public class RetypeCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _mycelium = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private static readonly Guid ThingId = Guid.NewGuid();
    private static readonly Guid IsId = Guid.NewGuid();
    private static readonly Guid TypeA = Guid.NewGuid();
    private static readonly Guid TypeB = Guid.NewGuid();
    private static readonly Guid NewType = Guid.NewGuid();
    private static readonly Guid RelA = Guid.NewGuid();
    private static readonly Guid RelB = Guid.NewGuid();

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;
    private static string Edge(Guid id, Guid subj, Guid pred, Guid tgt) =>
        $"{{\"Id\":\"{id}\",\"SubjectId\":\"{subj}\",\"PredicateId\":\"{pred}\",\"TargetId\":\"{tgt}\"}}";

    private Task Execute(string arg) => new RetypeCommandHandler(arg, _writer, _mycelium.Object).ExecuteAsync();

    private void SetupModel(params string[] edges)
    {
        _mycelium.Setup(m => m.GetAllThingsAsync()).ReturnsAsync(Json($"[{{\"Id\":\"{IsId}\",\"Name\":\"is\"}}]"));
        _mycelium.Setup(m => m.GetAllRelationshipsAsync()).ReturnsAsync(Json("[" + string.Join(",", edges) + "]"));
        _mycelium.Setup(m => m.DeleteRelationshipAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _mycelium.Setup(m => m.CreateRelationshipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(Json("{}"));
    }

    [Fact]
    public async Task Single_type_swap_replaces_the_one_is_edge()
    {
        SetupModel(Edge(RelA, ThingId, IsId, TypeA));
        await Execute($"{ThingId} {NewType}");
        _mycelium.Verify(m => m.DeleteRelationshipAsync(RelA), Times.Once);
        _mycelium.Verify(m => m.CreateRelationshipAsync(ThingId, IsId, NewType), Times.Once);
    }

    [Fact]
    public async Task Non_is_edges_are_left_untouched()
    {
        SetupModel(Edge(RelA, ThingId, Guid.NewGuid(), TypeA)); // a non-is edge
        await Execute($"{ThingId} {NewType}");
        _mycelium.Verify(m => m.DeleteRelationshipAsync(It.IsAny<Guid>()), Times.Never);
        _mycelium.Verify(m => m.CreateRelationshipAsync(ThingId, IsId, NewType), Times.Once);
    }

    [Fact]
    public async Task Two_arg_form_refuses_when_the_thing_has_multiple_types()
    {
        SetupModel(Edge(RelA, ThingId, IsId, TypeA), Edge(RelB, ThingId, IsId, TypeB));
        await Execute($"{ThingId} {NewType}");
        _writer.ToString().Should().Contain("multiple").And.Contain("retype <thing> <old> <new>");
        _mycelium.Verify(m => m.DeleteRelationshipAsync(It.IsAny<Guid>()), Times.Never);
        _mycelium.Verify(m => m.CreateRelationshipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Three_arg_form_replaces_only_the_named_type_and_keeps_the_others()
    {
        SetupModel(Edge(RelA, ThingId, IsId, TypeA), Edge(RelB, ThingId, IsId, TypeB));
        await Execute($"{ThingId} {TypeA} {NewType}");
        _mycelium.Verify(m => m.DeleteRelationshipAsync(RelA), Times.Once);
        _mycelium.Verify(m => m.DeleteRelationshipAsync(RelB), Times.Never, "the Thing's other type (B) must survive");
        _mycelium.Verify(m => m.CreateRelationshipAsync(ThingId, IsId, NewType), Times.Once);
    }

    [Fact]
    public async Task No_args_shows_usage()
    {
        await Execute("");
        _writer.ToString().Should().Contain("Usage");
    }
}
