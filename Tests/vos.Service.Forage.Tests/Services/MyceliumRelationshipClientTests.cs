using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

// The bodies here are written the way the broker answers — PascalCase, the naming policy it shares with
// the seed files — rather than the camelCase most of the platform's JSON reads in.
public class MyceliumRelationshipClientTests
{
    private const string CoverageName = "Willow Bend × NASA POWER climate averages";

    private static MyceliumRelationshipClient Client(
        HttpMessageHandler handler, ILogger<MyceliumRelationshipClient>? logger = null) =>
        new(new PerCallHttpClientFactory(handler),
            logger ?? NullLogger<MyceliumRelationshipClient>.Instance,
            "http://localhost:7243",
            new LaunchToken("test-token"));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task WriteCoverageAsync_SendsTheRecordAndItsRelationshipsAsOneFragmentUnderTheIdentifierGiven()
    {
        var (record, predicate, target) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        HttpRequestMessage? sent = null;
        string? body = null;
        var handler = new MockHttpMessageHandler(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, "{}");
        });

        var written = await Client(handler).WriteCoverageAsync(record, CoverageName, [(predicate, target)], default);

        written.Should().BeTrue();
        sent!.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.AbsolutePath.Should().Be("/api/model/fragment");
        using var fragment = System.Text.Json.JsonDocument.Parse(body!);
        var thing = fragment.RootElement.GetProperty("things").EnumerateArray().Should().ContainSingle().Subject;
        thing.GetProperty("id").GetGuid().Should().Be(record);
        thing.GetProperty("name").GetString().Should().Be(CoverageName);
        var relationship = fragment.RootElement.GetProperty("relationships").EnumerateArray().Should().ContainSingle().Subject;
        (relationship.GetProperty("subject").GetGuid(), relationship.GetProperty("predicate").GetGuid(), relationship.GetProperty("target").GetGuid())
            .Should().Be((record, predicate, target));
    }

    [Fact]
    public async Task WriteCoverageAsync_AnswersFalseAndSaysWhyWhenTheBrokerRefusesTheFragment()
    {
        var logger = new CapturingLogger<MyceliumRelationshipClient>();
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.BadRequest, """{"error":"no such predicate"}"""));

        var written = await Client(handler, logger).WriteCoverageAsync(Guid.NewGuid(), CoverageName, [], default);

        written.Should().BeFalse();
        logger.Lines.Should().ContainSingle().Which.Should().Contain("no such predicate");
    }

    [Fact]
    public async Task WriteCoverageAsync_AnswersFalseWhenTheBrokerCannotBeReached()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("the broker is not listening"));

        var written = await Client(handler).WriteCoverageAsync(Guid.NewGuid(), CoverageName, [], default);

        written.Should().BeFalse();
    }

    [Fact]
    public async Task CreateRelationshipAsync_RefusedWrite_LogsTheStatusAndTheModelsReason()
    {
        var logger = new CapturingLogger<MyceliumRelationshipClient>();
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.BadRequest,
            """
            {"error":"A relationship already exists with the same subject, predicate and target."}
            """));

        var related = await Client(handler, logger).CreateRelationshipAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default);

        related.Should().BeFalse();
        logger.Lines.Should().ContainSingle().Which.Should()
            .Contain("BadRequest").And.Contain("already exists",
                "a refusal logged without its reason cannot be told apart from any other");
    }
}
