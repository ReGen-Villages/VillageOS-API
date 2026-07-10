using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

// Covers the `retype <thing> <new-archetype>` command (US #5860): swap a Thing's is-edge.
public class RetypeCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _mycelium = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private static readonly Guid ThingId = Guid.NewGuid();
    private static readonly Guid IsId = Guid.NewGuid();
    private static readonly Guid OldType = Guid.NewGuid();
    private static readonly Guid NewType = Guid.NewGuid();
    private static readonly Guid OldRelId = Guid.NewGuid();

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    private Task Execute(string arg) =>
        new RetypeCommandHandler(arg, _writer, _mycelium.Object).ExecuteAsync();

    // One relationship: ThingId -[predicateId]-> OldType. When predicateId == IsId it is the type edge.
    private void SetupModel(Guid predicateId)
    {
        _mycelium.Setup(m => m.GetAllThingsAsync()).ReturnsAsync(Json(
            $"[{{\"Id\":\"{IsId}\",\"Name\":\"is\"}},{{\"Id\":\"{ThingId}\",\"Name\":\"El\"}},{{\"Id\":\"{NewType}\",\"Name\":\"SolarArray\"}}]"));
        _mycelium.Setup(m => m.GetAllRelationshipsAsync()).ReturnsAsync(Json(
            $"[{{\"Id\":\"{OldRelId}\",\"SubjectId\":\"{ThingId}\",\"PredicateId\":\"{predicateId}\",\"TargetId\":\"{OldType}\"}}]"));
        _mycelium.Setup(m => m.DeleteRelationshipAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        _mycelium.Setup(m => m.CreateRelationshipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(Json("{}"));
    }

    [Fact]
    public async Task Retype_removes_the_old_is_edge_and_adds_the_new_one()
    {
        SetupModel(predicateId: IsId);

        await Execute($"{ThingId} {NewType}");

        _mycelium.Verify(m => m.DeleteRelationshipAsync(OldRelId), Times.Once);
        _mycelium.Verify(m => m.CreateRelationshipAsync(ThingId, IsId, NewType), Times.Once);
        _writer.ToString().Should().Contain("Retyped");
    }

    [Fact]
    public async Task Retype_leaves_non_is_edges_untouched()
    {
        SetupModel(predicateId: Guid.NewGuid()); // a 'contains'-style edge, not an is-edge

        await Execute($"{ThingId} {NewType}");

        _mycelium.Verify(m => m.DeleteRelationshipAsync(It.IsAny<Guid>()), Times.Never);
        _mycelium.Verify(m => m.CreateRelationshipAsync(ThingId, IsId, NewType), Times.Once);
    }

    [Fact]
    public async Task Retype_no_args_shows_usage()
    {
        await Execute("");
        _writer.ToString().Should().Contain("Usage");
    }
}
