using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Services;
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
            "test-token");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task MintAsync_AnswersTheIdentifierTheBrokerNamedInItsOwnCasing()
    {
        var created = Guid.NewGuid();
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.OK,
            $$$"""
            {"Id":"{{{created}}}","Name":"{{{CoverageName}}}","IsArchetype":false,"Properties":{}}
            """));

        var minted = await Client(handler).MintAsync(CoverageName, default);

        minted.Should().Be(created);
    }

    [Fact]
    public async Task MintAsync_SaysTheThingIsUnreachableWhenTheCreatedThingNamesNoIdentifier()
    {
        var logger = new CapturingLogger<MyceliumRelationshipClient>();
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.OK,
            $$"""
            {"Name":"{{CoverageName}}"}
            """));

        var minted = await Client(handler, logger).MintAsync(CoverageName, default);

        minted.Should().BeNull();
        logger.Lines.Should().ContainSingle().Which.Should().Contain("named no identifier");
    }

    [Fact]
    public async Task MintAsync_SaysTheThingIsUnreachableWhenTheAnswerIsNotAThingAtAll()
    {
        var logger = new CapturingLogger<MyceliumRelationshipClient>();
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.OK, "[]"));

        var minted = await Client(handler, logger).MintAsync(CoverageName, default);

        minted.Should().BeNull();
        logger.Lines.Should().ContainSingle().Which.Should().Contain("named no identifier");
    }

    [Fact]
    public async Task MintAsync_AnswersNothingWhenTheBrokerRefusesTheCreate()
    {
        var handler = new MockHttpMessageHandler(_ => Json(HttpStatusCode.Conflict,
            """
            {"error":"Thing already exists"}
            """));

        var minted = await Client(handler).MintAsync(CoverageName, default);

        minted.Should().BeNull();
    }

    [Fact]
    public async Task MintAsync_AnswersNothingWhenTheBrokerCannotBeReached()
    {
        var handler = new MockHttpMessageHandler(
            _ => throw new HttpRequestException("the broker is not listening"));

        var minted = await Client(handler).MintAsync(CoverageName, default);

        minted.Should().BeNull();
    }
}
