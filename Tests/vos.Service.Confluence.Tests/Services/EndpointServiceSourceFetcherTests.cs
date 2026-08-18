using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Confluence.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Confluence.Tests.Services;

public class EndpointServiceSourceFetcherTests
{
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
            .FetchAsync("OpenMeteo", "OpenMeteoEndpoint", Coordinates, default);

        outcome.Resolved.Should().BeTrue();
        sent!.RequestUri!.AbsolutePath.Should().Be("/api/endpoints/fetcher");
        body.Should().Contain("OpenMeteoEndpoint").And.Contain("-25.75").And.Contain("28.19");
    }

    [Fact]
    public async Task FetchAsync_NonSuccess_ReportsTheStatusAndTheProvidersOwnWords()
    {
        var handler = new MockHttpMessageHandler(_ =>
            Text(HttpStatusCode.ServiceUnavailable, "portal is down for maintenance"));

        var outcome = await Fetcher(handler).FetchAsync("FloodPortal", "FloodEndpoint", Coordinates, default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("503").And.Contain("portal is down for maintenance");
    }

    [Fact]
    public async Task FetchAsync_VeryLongErrorBody_IsShortenedRatherThanCarriedWhole()
    {
        // A reason is read by a person deciding whether a gap matters, not a whole error document.
        var handler = new MockHttpMessageHandler(_ =>
            Text(HttpStatusCode.InternalServerError, new string('x', 5000)));

        var outcome = await Fetcher(handler).FetchAsync("Verbose", "VerboseEndpoint", Coordinates, default);

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
            .FetchAsync("Quiet", "QuietEndpoint", Coordinates, default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("No answer within");
    }

    [Fact]
    public async Task FetchAsync_TransportFailure_IsReportedRatherThanThrown()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));

        var outcome = await Fetcher(handler).FetchAsync("Broken", "BrokenEndpoint", Coordinates, default);

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
            .FetchAsync("Any", "AnyEndpoint", Coordinates, run.Token);
        await reached.Task;
        await run.CancelAsync();
        var outcome = await fetching;

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().NotContain("No answer within");
    }
}
