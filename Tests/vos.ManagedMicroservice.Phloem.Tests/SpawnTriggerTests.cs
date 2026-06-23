using System.Text.Json;
using FluentAssertions;
using vos.ManagedMicroservice.Phloem.Execution;
using Xunit;

namespace vos.ManagedMicroservice.Phloem.Tests;

// #5633: one /handle, two spawn shapes — http {pipelineId,params} (sync) and the graph `runs` relationship
// envelope {targetId, properties} (fire-and-forget). SpawnTrigger classifies and resolves the pipelineId.
public class SpawnTriggerTests
{
    private static readonly Guid Pipe = Guid.Parse("5628da90-0000-0000-0000-0000000000c1");
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Resolve_HttpBody_IsHttpSpawnWithParams()
    {
        var body = JsonSerializer.Serialize(new { pipelineId = Pipe.ToString(), @params = new { k = 1 } });
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Http);
        t.PipelineId.Should().Be(Pipe);
        t.Params.GetProperty("k").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Resolve_GraphRelationshipEnvelope_IsGraphSpawnTargetingThePipeline()
    {
        // What Mycelium forwards for `X runs Pipeline` (VosServiceBroker): target is the Pipeline,
        // the relationship's properties are the run params.
        var body = JsonSerializer.Serialize(new
        {
            relationshipId = "a0000000-0000-0000-0000-000000000001",
            subjectId = "b0000000-0000-0000-0000-000000000002",
            targetId = Pipe.ToString(),
            subjectName = "X",
            targetName = "Demo",
            properties = new { scenario = "base" },
        });
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Graph);
        t.PipelineId.Should().Be(Pipe);            // resolved from targetId
        t.Params.GetProperty("scenario").GetString().Should().Be("base");
    }

    [Theory]
    [InlineData("""{"params":{}}""")]                       // neither pipelineId nor targetId
    [InlineData("""{"pipelineId":"not-a-guid"}""")]          // bad http id
    [InlineData("""{"targetId":"not-a-guid"}""")]            // bad graph target
    [InlineData("""[]""")]                                    // not an object
    public void Resolve_BadBody_IsInvalidWithReason(string body)
    {
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Invalid);
        t.Error.Should().NotBeNullOrEmpty();
    }
}
