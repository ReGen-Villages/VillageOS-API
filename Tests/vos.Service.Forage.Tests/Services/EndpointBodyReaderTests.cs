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

public class EndpointBodyReaderTests
{
    private static readonly Dictionary<string, string> Coordinates =
        new() { ["lat"] = "-25.75", ["lng"] = "28.19" };

    private static EndpointBodyReader Reader(
        HttpMessageHandler handler, TimeSpan? timeout = null, string subdomain = "tributary", ILogger<EndpointBodyReader>? log = null) =>
        new(new PerCallHttpClientFactory(handler),
            log ?? NullLogger<EndpointBodyReader>.Instance,
            "http://localhost:7243",
            new LaunchToken("test-token"),
            subdomain,
            timeout ?? TimeSpan.FromSeconds(30));

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    [Fact]
    public async Task ReadAsync_ForwardsThroughTheConfiguredSubdomain()
    {
        string? path = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            path = req.RequestUri!.AbsolutePath;
            return Text(HttpStatusCode.OK, "{}");
        });

        await Reader(handler, subdomain: "outside-calls").ReadAsync("hazard-division-search", Coordinates, default);

        path.Should().Be("/api/endpoints/outside-calls");
    }

    [Fact]
    public async Task ReadAsync_AnswersTheProvidersOwnBodyAndNamesNoSubject()
    {
        string? body = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Text(HttpStatusCode.OK, """{"data": []}""");
        });

        var answered = await Reader(handler).ReadAsync("hazard-division-search", Coordinates, default);

        answered.Should().Be("""{"data": []}""");
        body.Should().Contain("hazard-division-search").And.NotContain("subjectId");
    }

    // A provider that could not be reached must never read as one that answered nothing: the first is
    // asked again, and the second would settle the question for good.
    [Fact]
    public async Task ReadAsync_ARefusedCallAnswersNothing()
    {
        var handler = new MockHttpMessageHandler(_ =>
            Text(HttpStatusCode.ServiceUnavailable, "portal is down for maintenance"));

        var answered = await Reader(handler).ReadAsync("hazard-division-search", Coordinates, default);

        answered.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_AnAnsweredCallWithAnEmptyBodyAnswersTheEmptyText()
    {
        var handler = new MockHttpMessageHandler(_ => Text(HttpStatusCode.OK, ""));

        var answered = await Reader(handler).ReadAsync("hazard-division-search", Coordinates, default);

        answered.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "bad request: /search?lat=-25.75&lng=28.19&key=made-up-key")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "bad request: /search?lat=-25.75&lng=28.19&key=made-up-key")]
    [InlineData(HttpStatusCode.Unauthorized, "bad request\n/search?lat=-25.75\n&lng=28.19\n&key=made-up-key\n")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "bad request\n/search?lat=-25.75\n&lng=28.19\n&key=made-up-key\n")]
    public async Task ReadAsync_ARefusedCallLogsItsEndpointSubdomainAndStatus_AndNothingOfTheAnswer(HttpStatusCode status, string body)
    {
        var log = new CapturingLogger<EndpointBodyReader>();

        await Reader(new MockHttpMessageHandler(_ => Text(status, body)), log: log).ReadAsync("hazard-division-search", Coordinates, default);

        log.Lines.Should().Equal($"Reading hazard-division-search through tributary was answered {(int)status}");
    }

    [Fact]
    public async Task ReadAsync_AnUnreachableFetchingServiceAnswersNothing()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));

        var answered = await Reader(handler).ReadAsync("area-name-at-position", Coordinates, default);

        answered.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_AProviderThatGoesQuietAnswersNothing()
    {
        var handler = MockHttpMessageHandler.ObservingCancellation(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var answered = await Reader(handler, TimeSpan.FromMilliseconds(50))
            .ReadAsync("area-name-at-position", Coordinates, default);

        answered.Should().BeNull();
    }
}
