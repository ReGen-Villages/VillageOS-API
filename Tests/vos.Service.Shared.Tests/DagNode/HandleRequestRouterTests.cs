using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared.DagNode;
using Xunit;

namespace vos.Service.Shared.Tests.DagNode;

public class HandleRequestRouterTests
{
    [Fact]
    public void Classify_WithRunIdAndNodeId_IsANodeEnvelope()
    {
        var request = HandleRequestRouter.Classify("""{"runId":"a1b2","nodeId":"n1","inputs":{}}""");

        request.Kind.Should().Be(HandleRequestKind.NodeEnvelope);
        request.SubjectId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Classify_PrefersTheNodeEnvelopeWhenABodyCarriesBothShapes()
    {
        var request = HandleRequestRouter.Classify(
            """{"runId":"a1b2","nodeId":"n1","subjectId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}""");

        request.Kind.Should().Be(HandleRequestKind.NodeEnvelope);
    }

    [Theory]
    [InlineData("""{"runId":"a1b2"}""")]
    [InlineData("""{"nodeId":"n1"}""")]
    public void Classify_WithOnlyHalfOfTheEnvelope_IsNotANodeEnvelope(string json) =>
        HandleRequestRouter.Classify(json).Kind.Should().Be(HandleRequestKind.Unrecognised);

    [Fact]
    public void Classify_WithASubjectId_NamesTheThingToActOn()
    {
        var request = HandleRequestRouter.Classify("""{"subjectId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}""");

        request.Kind.Should().Be(HandleRequestKind.RelationshipSubject);
        request.SubjectId.Should().Be(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
    }

    [Theory]
    [InlineData("subjectId")]
    [InlineData("SubjectId")]
    [InlineData("SUBJECTID")]
    [InlineData("subjectid")]
    public void Classify_ReadsTheSubjectIdWhateverItsCasing(string propertyName)
    {
        var request = HandleRequestRouter.Classify(
            $$"""{"{{propertyName}}":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}""");

        request.Kind.Should().Be(HandleRequestKind.RelationshipSubject);
        request.SubjectId.Should().Be(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
    }

    [Theory]
    [InlineData("""{"subjectId":"not-a-guid"}""")]
    [InlineData("""{"subjectId":42}""")]
    [InlineData("""{"subjectId":null}""")]
    [InlineData("""{"subjectId":""}""")]
    public void Classify_WithASubjectIdThatIsNotAnIdentifier_IsUnrecognised(string json)
    {
        var request = HandleRequestRouter.Classify(json);

        request.Kind.Should().Be(HandleRequestKind.Unrecognised);
        request.SubjectId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Classify_WithAnEmptyObject_IsUnrecognised() =>
        HandleRequestRouter.Classify("{}").Kind.Should().Be(HandleRequestKind.Unrecognised);

    [Theory]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("true")]
    public void Classify_WhenTheBodyIsNotAnObject_IsUnrecognised(string json)
    {
        var request = HandleRequestRouter.Classify(json);

        request.Kind.Should().Be(HandleRequestKind.Unrecognised);
        request.SubjectId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Classify_FindsTheSubjectIdAmongOtherProperties()
    {
        var request = HandleRequestRouter.Classify(
            """{"other":"x","subjectId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","more":1}""");

        request.Kind.Should().Be(HandleRequestKind.RelationshipSubject);
        request.SubjectId.Should().Be(Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"));
    }

    // Text a service cannot read at all is the same answer as JSON it can read and cannot act on:
    // a body it refuses, not a request that fails.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not json")]
    [InlineData("""{"subjectId":""")]
    [InlineData("""{"subjectId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"} and then some""")]
    public void Classify_WhenTheBodyIsNotJson_IsUnrecognisedRatherThanRaising(string? body)
    {
        var request = HandleRequestRouter.Classify(body);

        request.Kind.Should().Be(HandleRequestKind.Unrecognised);
        request.SubjectId.Should().Be(Guid.Empty);
        request.IsJson.Should().BeFalse();
    }

    [Fact]
    public void Classify_HandsBackABodyThatOutlivesTheDocumentItWasReadFrom()
    {
        var request = HandleRequestRouter.Classify("""{"runId":"a1b2","nodeId":"n1","inputs":{"depth":3}}""");

        request.IsJson.Should().BeTrue();
        request.Json.GetProperty("inputs").GetProperty("depth").GetInt32().Should().Be(3);
    }

    [Fact]
    public void DescribeExpectedShapes_NamesTheServiceAndBothShapes()
    {
        var message = HandleRequestRouter.DescribeExpectedShapes("EnergyBalance");

        message.Should().Contain("EnergyBalance").And.Contain("runId").And.Contain("subjectId");
    }

    [Fact]
    public void DescribeExpectedNodeEnvelope_NamesTheServiceAndTheEnvelope()
    {
        var message = HandleRequestRouter.DescribeExpectedNodeEnvelope("ModelBridge");

        message.Should().Contain("ModelBridge").And.Contain("runId").And.Contain("nodeId");
    }
}
