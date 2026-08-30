using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

public class EndpointServiceSourceFetcherTests
{
    private static readonly Guid Site = Guid.NewGuid();

    private static readonly Dictionary<string, string> Coordinates =
        new() { ["lat"] = "-25.75", ["lng"] = "28.19" };

    private sealed class PerCallFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static EndpointServiceSourceFetcher Fetcher(
        HttpMessageHandler handler, TimeSpan? timeout = null, string subdomain = "tributary") =>
        new(new PerCallFactory(handler),
            NullLogger<EndpointServiceSourceFetcher>.Instance,
            "http://localhost:7243",
            "test-token",
            subdomain,
            timeout ?? TimeSpan.FromSeconds(30));

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    [Fact]
    public async Task FetchAsync_ForwardsThroughTheConfiguredSubdomain()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            sent = req;
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Text(HttpStatusCode.OK, "{}");
        });

        var outcome = await Fetcher(handler, subdomain: "fetcher")
            .FetchAsync(Site, "OpenMeteo", "OpenMeteoEndpoint", Coordinates, default);

        outcome.Resolved.Should().BeTrue();
        sent!.RequestUri!.AbsolutePath.Should().Be("/api/endpoints/fetcher");
        body.Should().Contain("OpenMeteoEndpoint").And.Contain("-25.75").And.Contain("28.19");
    }

    [Fact]
    public async Task FetchAsync_NamesTheSiteTheCallIsAbout()
    {
        // One registration serves every site, so the reading's destination cannot come from the
        // registration. Without this the fetcher's own expression would decide, and every site's
        // values would land on whichever Thing that expression names (Bug #6532).
        string? body = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Text(HttpStatusCode.OK, "{}");
        });

        await Fetcher(handler).FetchAsync(Site, "OpenMeteo", "OpenMeteoEndpoint", Coordinates, default);

        body.Should().Contain("subjectId").And.Contain(Site.ToString());
    }

    [Fact]
    public async Task FetchAsync_NonSuccess_ReportsTheStatusAndTheProvidersOwnWords()
    {
        var handler = new MockHttpMessageHandler(_ =>
            Text(HttpStatusCode.ServiceUnavailable, "portal is down for maintenance"));

        var outcome = await Fetcher(handler).FetchAsync(Site, "FloodPortal", "FloodEndpoint", Coordinates, default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("503").And.Contain("portal is down for maintenance");
    }

    [Fact]
    public async Task FetchAsync_VeryLongErrorBody_IsShortenedRatherThanCarriedWhole()
    {
        // A reason is read by a person deciding whether a gap matters, not a whole error document.
        var handler = new MockHttpMessageHandler(_ =>
            Text(HttpStatusCode.InternalServerError, new string('x', 5000)));

        var outcome = await Fetcher(handler).FetchAsync(Site, "Verbose", "VerboseEndpoint", Coordinates, default);

        outcome.Reason!.Length.Should().BeLessThan(500);
        outcome.Reason.Should().EndWith("…");
    }

    [Fact]
    public async Task FetchAsync_SourceThatNeverAnswers_IsBoundedByTheTimeout()
    {
        // A provider that accepts the connection and then goes quiet is more common than one that
        // refuses outright, and it is the failure most likely to be met in production.
        var handler = MockHttpMessageHandler.ObservingCancellation(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var outcome = await Fetcher(handler, TimeSpan.FromMilliseconds(150))
            .FetchAsync(Site, "Quiet", "QuietEndpoint", Coordinates, default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("No answer within");
    }

    [Fact]
    public async Task FetchAsync_TransportFailure_IsReportedRatherThanThrown()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));

        var outcome = await Fetcher(handler).FetchAsync(Site, "Broken", "BrokenEndpoint", Coordinates, default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("connection reset");
    }

    [Fact]
    public async Task FetchAsync_RunCancelledMidFlight_DoesNotReadAsATimeout()
    {
        // A cancelled run is the caller withdrawing, not a provider going quiet. Reporting it as a
        // timeout would blame the source for something it never did, and would put a fabricated
        // outage in a report a planner reads.
        using var run = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        var handler = MockHttpMessageHandler.ObservingCancellation(async (_, token) =>
        {
            reached.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        // A source timeout far longer than the test, so only the run's own cancellation can end it.
        var fetching = Fetcher(handler, TimeSpan.FromMinutes(5))
            .FetchAsync(Site, "Any", "AnyEndpoint", Coordinates, run.Token);
        await reached.Task;
        await run.CancelAsync();
        var outcome = await fetching;

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().NotContain("No answer within");
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

        var answered = await Fetcher(handler)
            .ReadAsync("hazard-division-search", Coordinates, default);

        answered.Should().Be("""{"data": []}""");
        body.Should().Contain("hazard-division-search").And.NotContain("subjectId");
    }

    // A provider that could not be reached must never read as one that answered nothing: the first is
    // asked again on the next run, and the second would settle the question for good.
    [Fact]
    public async Task ReadAsync_ARefusedCallAnswersNothing()
    {
        var handler = new MockHttpMessageHandler(_ =>
            Text(HttpStatusCode.ServiceUnavailable, "portal is down for maintenance"));

        var answered = await Fetcher(handler).ReadAsync("hazard-division-search", Coordinates, default);

        answered.Should().BeNull();
    }

    [Fact]
    public async Task ReadAsync_AnUnreachableFetchingServiceAnswersNothing()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));

        var answered = await Fetcher(handler).ReadAsync("area-name-at-position", Coordinates, default);

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

        var answered = await Fetcher(handler, TimeSpan.FromMilliseconds(50))
            .ReadAsync("area-name-at-position", Coordinates, default);

        answered.Should().BeNull();
    }
}
