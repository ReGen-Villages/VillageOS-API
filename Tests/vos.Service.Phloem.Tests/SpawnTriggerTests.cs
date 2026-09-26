using System.Text.Json;
using FluentAssertions;
using vos.Service.Phloem.Execution;
using Xunit;

namespace vos.Service.Phloem.Tests;

// One /handle, two spawn shapes — http {pipelineId,params} (sync) and a dispatched relationship
// {subjectId, targetId, properties} (fire-and-forget). SpawnTrigger classifies; which pipeline a
// relationship's target starts is the model's answer, not the body's.
public class SpawnTriggerTests
{
    private static readonly Guid Pipe = Guid.Parse("5628da90-0000-0000-0000-0000000000c1");
    private static readonly Guid Subject = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Resolve_HttpBody_IsHttpSpawnWithParams()
    {
        var body = JsonSerializer.Serialize(new { pipelineId = Pipe.ToString(), @params = new { k = 1 } });
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Http);
        t.PipelineId.Should().Be(Pipe);
        t.Params.GetProperty("k").GetInt32().Should().Be(1);
        t.Async.Should().BeFalse(); // synchronous spawn-and-wait by default
    }

    [Fact]
    public void Resolve_HttpBodyWithAsync_IsAsyncHttpSpawn()
    {
        // The editor's Run uses an async spawn so it gets the run id up front and animates over SSE.
        var body = JsonSerializer.Serialize(new { pipelineId = Pipe.ToString(), @async = true });
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Http);
        t.Async.Should().BeTrue();
    }

    [Fact]
    public void Resolve_DispatchedRelationship_CarriesTargetSubjectAndPropertiesAsParams()
    {
        // What Mycelium posts for `X runs Pipeline` and for a Thing entering a watched state
        // (VosServiceBroker): the target is the pipeline in the first and the watching connection in the
        // second, the subject is what entered, and the relationship's properties are the run params.
        var body = JsonSerializer.Serialize(new
        {
            relationshipId = "a0000000-0000-0000-0000-000000000001",
            subjectId = Subject.ToString(),
            targetId = Pipe.ToString(),
            subjectName = "Submission 42",
            targetName = "Demo",
            properties = new { scenario = "base" },
        });
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Relationship);
        t.TargetId.Should().Be(Pipe);
        t.Subject.Should().Be(new RunSubject(Subject, "Submission 42"));
        t.Params.GetProperty("scenario").GetString().Should().Be("base");
        t.Async.Should().BeTrue();                 // a dispatched relationship is always fire-and-forget
    }

    // A body naming no subject, or one that is not an identifier, is still a relationship to act on: the
    // run simply has no subject to record.
    [Theory]
    [InlineData("""{"targetId":"5628da90-0000-0000-0000-0000000000c1"}""")]
    [InlineData("""{"targetId":"5628da90-0000-0000-0000-0000000000c1","subjectId":"not-a-guid"}""")]
    public void Resolve_DispatchedRelationshipWithoutASubject_HasNone(string body)
    {
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Relationship);
        t.Subject.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"params":{}}""")]                       // neither pipelineId nor targetId
    [InlineData("""{"pipelineId":"not-a-guid"}""")]          // bad http id
    [InlineData("""{"targetId":"not-a-guid"}""")]            // bad relationship target
    [InlineData("""[]""")]                                    // not an object
    public void Resolve_BadBody_IsInvalidWithReason(string body)
    {
        var t = SpawnTrigger.Resolve(Json(body));

        t.Kind.Should().Be(SpawnKind.Invalid);
        t.Error.Should().NotBeNullOrEmpty();
    }
}
