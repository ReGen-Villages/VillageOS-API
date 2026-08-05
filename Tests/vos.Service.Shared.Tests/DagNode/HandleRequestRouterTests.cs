using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared.DagNode;
using Xunit;

namespace vos.Service.Shared.Tests.DagNode;

public class HandleRequestRouterTests
{
    private static JsonElement Body(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Classify_WithRunIdAndNodeId_IsANodeEnvelope()
    {
        var kind = HandleRequestRouter.Classify(
            Body("""{"runId":"a1b2","nodeId":"n1","inputs":{}}"""), out var subjectId);

        kind.Should().Be(HandleRequestKind.NodeEnvelope);
        subjectId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Classify_PrefersTheNodeEnvelopeWhenABodyCarriesBothShapes()
    {
        var kind = HandleRequestRouter.Classify(
            Body("""{"runId":"a1b2","nodeId":"n1","subjectId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}"""),
            out _);

        kind.Should().Be(HandleRequestKind.NodeEnvelope);
    }

    [Theory]
    [InlineData("""{"runId":"a1b2"}""")]
    [InlineData("""{"nodeId":"n1"}""")]
    public void Classify_WithOnlyHalfOfTheEnvelope_IsNotANodeEnvelope(string json)
    {
        var kind = HandleRequestRouter.Classify(Body(json), out _);

        kind.Should().Be(HandleRequestKind.Unrecognised);
    }

    [Fact]
    public void Classify_WithASubjectId_NamesTheThingToActOn()
    {
        var kind = HandleRequestRouter.Classify(
            Body("""{"subjectId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}"""), out var subjectId);

        kind.Should().Be(HandleRequestKind.RelationshipSubject);
        subjectId.Should().Be(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
    }

    [Theory]
    [InlineData("subjectId")]
    [InlineData("SubjectId")]
    [InlineData("SUBJECTID")]
    [InlineData("subjectid")]
    public void Classify_ReadsTheSubjectIdWhateverItsCasing(string propertyName)
    {
        var kind = HandleRequestRouter.Classify(
            Body($$"""{"{{propertyName}}":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}"""), out var subjectId);

        kind.Should().Be(HandleRequestKind.RelationshipSubject);
        subjectId.Should().Be(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
    }

    [Theory]
    [InlineData("""{"subjectId":"not-a-guid"}""")]
    [InlineData("""{"subjectId":42}""")]
    [InlineData("""{"subjectId":null}""")]
    [InlineData("""{"subjectId":""}""")]
    public void Classify_WithASubjectIdThatIsNotAnIdentifier_IsUnrecognised(string json)
    {
        var kind = HandleRequestRouter.Classify(Body(json), out var subjectId);

        kind.Should().Be(HandleRequestKind.Unrecognised);
        subjectId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Classify_WithAnEmptyObject_IsUnrecognised() =>
        HandleRequestRouter.Classify(Body("{}"), out _).Should().Be(HandleRequestKind.Unrecognised);

    [Theory]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("true")]
    public void Classify_WhenTheBodyIsNotAnObject_IsUnrecognised(string json)
    {
        var kind = HandleRequestRouter.Classify(Body(json), out var subjectId);

        kind.Should().Be(HandleRequestKind.Unrecognised);
        subjectId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryReadSubjectId_TakesTheFirstPropertyThatCarriesAnIdentifier()
    {
        var found = HandleRequestRouter.TryReadSubjectId(
            Body("""{"other":"x","subjectId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}"""),
            out var subjectId);

        found.Should().BeTrue();
        subjectId.Should().Be(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
    }

    [Fact]
    public void DescribeExpectedShapes_NamesTheServiceAndBothShapes()
    {
        var message = HandleRequestRouter.DescribeExpectedShapes("WaterReserve");

        message.Should().Contain("WaterReserve").And.Contain("runId").And.Contain("subjectId");
    }

    [Fact]
    public void DescribeExpectedNodeEnvelope_NamesTheServiceAndTheEnvelope()
    {
        var message = HandleRequestRouter.DescribeExpectedNodeEnvelope("ModelBridge");

        message.Should().Contain("ModelBridge").And.Contain("runId").And.Contain("nodeId");
    }
}
