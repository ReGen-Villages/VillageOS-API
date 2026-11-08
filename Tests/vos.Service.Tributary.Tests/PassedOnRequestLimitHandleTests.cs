using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Shared;
using vos.Service.Tributary.Services;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Tributary.Tests.MyceliumStub;

namespace vos.Service.Tributary.Tests;

public class PassedOnRequestLimitHandleTests
{
    private const string PassedOnRequestLimitSeconds = "30";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(28);
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47 };

    private const string ReshapedProperties = """
        {
          "Endpoint.url":               {"Value":"https://api.test/point"},
          "Endpoint.httpMethod":        {"Value":"GET"},
          "Endpoint.responseTransform": {"Value":"{\"properties\": {\"flowRegime\": regime}}"}
        }
        """;

    [Fact]
    public async Task Handle_WhenTheBrokerHasNotAcceptedTheReadingsByTheDeadline_AnswersSoBeforeMyceliumGivesUp()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        const string props = ReshapedProperties;

        await AssertAnsweredAtTheDeadlineAsync(
            stalls: request => IsObservationWrite(request, subjectId),
            answers: request => request.RequestUri!.Host == "api.test"
                ? Json("""{"regime":"steady"}""")
                : RouteFindThing(request, thingId, "EP")
                  ?? RouteEffectiveProperties(request, thingId, props)
                  ?? RouteKindsFromProperties(request, thingId, props)
                  ?? RouteRelationshipWrite(request),
            handleRequest: new { endpointName = "EP", subjectId },
            reason: "The broker had not accepted the readings within 28 of the 30 seconds");
    }

    [Fact]
    public async Task Handle_WhenTheDataSourceHasNotAnsweredByTheDeadline_AnswersSo()
    {
        var thingId = Guid.NewGuid();
        const string props = ReshapedProperties;

        await AssertAnsweredAtTheDeadlineAsync(
            stalls: request => request.RequestUri!.Host == "api.test",
            answers: request => RouteFindThing(request, thingId, "EP")
                ?? RouteEffectiveProperties(request, thingId, props)
                ?? RouteKindsFromProperties(request, thingId, props),
            handleRequest: new { endpointName = "EP" },
            reason: "The data source had not answered within 28 of the 30 seconds");
    }

    [Fact]
    public async Task Handle_WhenTheBrokerHasNotAnsweredWithTheEndpointsSettingsByTheDeadline_AnswersSo()
    {
        var thingId = Guid.NewGuid();

        await AssertAnsweredAtTheDeadlineAsync(
            stalls: request => request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/properties",
            answers: request => RouteFindThing(request, thingId, "EP"),
            handleRequest: new { endpointName = "EP" },
            reason: "The broker had not answered with the settings of endpoint EP within 28 of the 30 seconds");
    }

    [Fact]
    public async Task Handle_WhenTheBrokerHasNotAnsweredWhichKindsTheEndpointReachesByTheDeadline_AnswersSo()
    {
        var thingId = Guid.NewGuid();
        const string props = ReshapedProperties;

        await AssertAnsweredAtTheDeadlineAsync(
            stalls: request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/subscriptions",
            answers: request => RouteFindThing(request, thingId, "EP")
                ?? RouteEffectiveProperties(request, thingId, props),
            handleRequest: new { endpointName = "EP" },
            reason: "The broker had not answered with the settings of endpoint EP within 28 of the 30 seconds");
    }

    [Fact]
    public async Task Handle_WhenTheDataSourcesSignInHasNotAnsweredByTheDeadline_AnswersSo()
    {
        var thingId = Guid.NewGuid();
        const string props = """
        {
          "Endpoint.url":        {"Value":"https://features.test/query"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.authKind":   {"Value":"tokenExchange"},
          "Esri.tokenUrl":       {"Value":"https://tokens.test/generateToken"},
          "Esri.tokenRequest":   {"Value":{"username":"alice","password":"s3cret","f":"json"}},
          "Esri.tokenPath":      {"Value":"token"},
          "Esri.expiryPath":     {"Value":"expires"},
          "Esri.expiryUnit":     {"Value":"epochMillis"}
        }
        """;

        await AssertAnsweredAtTheDeadlineAsync(
            stalls: request => request.RequestUri!.Host == "tokens.test",
            answers: request => RouteFindThing(request, thingId, "EP")
                ?? RouteEffectiveProperties(request, thingId, props)
                ?? RouteKindsFromProperties(request, thingId, props),
            handleRequest: new { endpointName = "EP" },
            reason: "The data source had not answered within 28 of the 30 seconds");
    }

    [Fact]
    public async Task Handle_WhenTheBrokerHasNotKeptTheResponseByTheDeadline_AnswersSo()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        Kind[] kinds =
        [
            new Kind(EndpointKindRoles.ResponseBody, "BinaryResponse"),
            new Kind(EndpointKindRoles.Keeping, "ModelAsset", "assetProperty", "assetSubject"),
        ];

        await AssertAnsweredAtTheDeadlineAsync(
            stalls: request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/assets",
            answers: request => request.RequestUri!.Host == "tiles.test"
                ? Binary(PngBytes, "image/png")
                : RouteFindThing(request, thingId, "EP")
                  ?? RouteEffectiveProperties(request, thingId, props)
                  ?? RouteKinds(request, thingId, kinds),
            handleRequest: new { endpointName = "EP" },
            reason: "The broker had not kept the response within 28 of the 30 seconds");
    }

    [Fact]
    public async Task Handle_WhenTheBrokerAcceptsTheReadingsAsTheDeadlinePasses_AnswersThatTheyWereIngested()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        const string props = ReshapedProperties;
        StalledHost? host = null;

        await using (host = await StalledHost.StartAsync(
            PassedOnRequestLimitSeconds,
            stalls: _ => false,
            answers: request =>
            {
                if (IsObservationWrite(request, subjectId))
                {
                    host!.Clock.Advance(Deadline);
                    return Json("""{"accepted":1}""");
                }
                return request.RequestUri!.Host == "api.test"
                    ? Json("""{"regime":"steady"}""")
                    : RouteFindThing(request, thingId, "EP")
                      ?? RouteEffectiveProperties(request, thingId, props)
                      ?? RouteKindsFromProperties(request, thingId, props)
                      ?? RouteRelationshipWrite(request);
            }))
        {
            var response = await host.Client.PostAsJsonAsync("/handle", new { endpointName = "EP", subjectId });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("\"observationsSubmitted\":1");
        }
    }

    [Theory]
    [InlineData("2", 2, true)]
    [InlineData("3", 1, false)]
    public async Task Handle_AtAShortLimit_TakesTheWholeLimitOnlyWhenTwoSecondsWouldLeaveNone(
        string passedOnRequestLimitSeconds, int deadlineSeconds, bool warnsAtStartUp)
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        const string props = ReshapedProperties;
        var deadline = TimeSpan.FromSeconds(deadlineSeconds);

        await using var host = await StalledHost.StartAsync(
            passedOnRequestLimitSeconds,
            stalls: request => IsObservationWrite(request, subjectId),
            answers: request => request.RequestUri!.Host == "api.test"
                ? Json("""{"regime":"steady"}""")
                : RouteFindThing(request, thingId, "EP")
                  ?? RouteEffectiveProperties(request, thingId, props)
                  ?? RouteKindsFromProperties(request, thingId, props)
                  ?? RouteRelationshipWrite(request));
        var warningsAtStartUp = host.LimitLog.Lines.Count;

        var answer = host.Client.PostAsJsonAsync("/handle", new { endpointName = "EP", subjectId });
        await Settle.ForAsync(host.Stalled, "the readings reach the broker");
        host.Clock.Advance(deadline - OneTick);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        answer.IsCompleted.Should().BeFalse("the deadline has not yet passed");
        host.Clock.Advance(OneTick);
        await Settle.ForAsync(answer, "Tributary answers at its deadline");

        (await (await answer).Content.ReadAsStringAsync()).Should().Contain(
            $"within {deadlineSeconds} of the {passedOnRequestLimitSeconds} seconds");
        if (warnsAtStartUp)
        {
            warningsAtStartUp.Should().Be(1, "the warning is logged when Tributary starts, not on a request");
            host.LimitLog.Lines.Should().ContainSingle().Which.Should()
                .Contain($"allows {passedOnRequestLimitSeconds} seconds for a passed-on request")
                .And.Contain("DaemonLauncher:PassedOnRequestLimitSeconds above 2");
        }
        else
        {
            host.LimitLog.Lines.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Handle_WhenTheDataSourceRefusesAsTheDeadlinePasses_PassesItsRefusalOn()
    {
        var thingId = Guid.NewGuid();
        const string props = ReshapedProperties;
        StalledHost? host = null;

        await using (host = await StalledHost.StartAsync(
            PassedOnRequestLimitSeconds,
            stalls: _ => false,
            answers: request =>
            {
                if (request.RequestUri!.Host == "api.test")
                {
                    host!.Clock.Advance(Deadline);
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent("""{"error":"slow down"}""")
                    };
                }
                return RouteFindThing(request, thingId, "EP")
                    ?? RouteEffectiveProperties(request, thingId, props)
                    ?? RouteKindsFromProperties(request, thingId, props);
            }))
        {
            var response = await host.Client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await response.Content.ReadAsStringAsync()).Should().Contain("slow down");
        }
    }

    [Fact]
    public async Task Handle_WhenABrokerWriteFailsBeforeTheDeadline_StillLogsItsError()
    {
        var thingId = Guid.NewGuid();
        var props = """
        {
          "Endpoint.url":           {"Value":"https://tiles.test/tile/1/0/1"},
          "Endpoint.httpMethod":    {"Value":"GET"},
          "Endpoint.assetProperty": {"Value":"surfaceMap"},
          "Endpoint.assetSubject":  {"Value":"SiteAlpha"}
        }
        """;
        Kind[] kinds =
        [
            new Kind(EndpointKindRoles.ResponseBody, "BinaryResponse"),
            new Kind(EndpointKindRoles.Keeping, "ModelAsset", "assetProperty", "assetSubject"),
        ];

        await using var host = await StalledHost.StartAsync(
            PassedOnRequestLimitSeconds,
            stalls: _ => false,
            answers: request =>
            {
                if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/assets")
                    throw new HttpRequestException("The broker closed the connection.");
                return request.RequestUri!.Host == "tiles.test"
                    ? Binary(PngBytes, "image/png")
                    : RouteFindThing(request, thingId, "EP")
                      ?? RouteEffectiveProperties(request, thingId, props)
                      ?? RouteKinds(request, thingId, kinds);
            });

        var response = await host.Client.PostAsJsonAsync("/handle", new { endpointName = "EP" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        host.BrokerLog.ErrorLines.Should().ContainSingle().Which.Should().Contain("The broker closed the connection.");
        host.Log.Lines.Should().NotContain(line => line.Contains("at its deadline"));
    }

    [Fact]
    public async Task Handle_WithNoPassedOnRequestLimit_SetsNoDeadline()
    {
        var thingId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        const string props = ReshapedProperties;
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await StalledHost.StartAsync(
            passedOnRequestLimitSeconds: null,
            stalls: request => IsObservationWrite(request, subjectId),
            answers: request => request.RequestUri!.Host == "api.test"
                ? Json("""{"regime":"steady"}""")
                : RouteFindThing(request, thingId, "EP")
                  ?? RouteEffectiveProperties(request, thingId, props)
                  ?? RouteKindsFromProperties(request, thingId, props)
                  ?? RouteRelationshipWrite(request)
                  ?? RouteObservationWrite(request, subjectId),
            released: released.Task);

        var answer = host.Client.PostAsJsonAsync("/handle", new { endpointName = "EP", subjectId });
        await Settle.ForAsync(host.Stalled, "the readings reach the broker");
        host.Clock.Advance(TimeSpan.FromHours(1));
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        answer.IsCompleted.Should().BeFalse("no deadline is set when the platform gives no limit");

        released.SetResult();
        await Settle.ForAsync(answer, "the broker accepts the readings");

        (await answer).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Handle_WhenAnEndpointsTimeoutIsNotShorterThanThePassedOnRequestLimit_WarnsTheFirstTimeOnly()
    {
        var log = await CalledTwiceAsync(timeout: "30", PassedOnRequestLimitSeconds);

        log.Lines.Should().ContainSingle().Which.Should()
            .Contain("EP").And.Contain("timeout").And.Contain("30 seconds").And.Contain("below 30 seconds");
    }

    [Fact]
    public async Task Handle_WhenAnEndpointsTimeoutIsShorterThanThePassedOnRequestLimit_IsSilent()
    {
        var log = await CalledTwiceAsync(timeout: "29", PassedOnRequestLimitSeconds);

        log.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenThePlatformGivesNoPassedOnRequestLimit_IsSilentAboutAnyTimeout()
    {
        var log = await CalledTwiceAsync(timeout: "60", passedOnRequestLimitSeconds: null);

        log.Lines.Should().BeEmpty();
    }

    private static async Task<CapturingLogger<PassedOnRequestLimit>> CalledTwiceAsync(
        string timeout, string? passedOnRequestLimitSeconds)
    {
        var thingId = Guid.NewGuid();
        var props = $$"""
        {
          "Endpoint.url":        {"Value":"https://api.test/x"},
          "Endpoint.httpMethod": {"Value":"GET"},
          "Endpoint.timeout":    {"Value":{{timeout}}}
        }
        """;
        var log = new CapturingLogger<PassedOnRequestLimit>();
        await using var factory = new TributaryWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request => request.RequestUri!.Host == "api.test"
            ? Json("""{"ok":true}""")
            : RouteFindThing(request, thingId, "EP")
              ?? RouteEffectiveProperties(request, thingId, props)
              ?? RouteKindsFromProperties(request, thingId, props)
              ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            if (passedOnRequestLimitSeconds is not null)
                builder.UseSetting("PassedOnRequestLimitSeconds", passedOnRequestLimitSeconds);
            builder.ConfigureTestServices(services => services.AddSingleton<ILogger<PassedOnRequestLimit>>(log));
        });
        using var client = host.CreateClient();

        (await client.PostAsJsonAsync("/handle", new { endpointName = "EP" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/handle", new { endpointName = "EP" })).StatusCode.Should().Be(HttpStatusCode.OK);
        return log;
    }

    private static bool IsObservationWrite(HttpRequestMessage request, Guid thingId) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == $"/api/things/{thingId}/observations";

    private static async Task AssertAnsweredAtTheDeadlineAsync(
        Func<HttpRequestMessage, bool> stalls,
        Func<HttpRequestMessage, HttpResponseMessage?> answers,
        object handleRequest,
        string reason)
    {
        await using var host = await StalledHost.StartAsync(PassedOnRequestLimitSeconds, stalls, answers);

        var answer = host.Client.PostAsJsonAsync("/handle", handleRequest);
        await Settle.ForAsync(host.Stalled, "the request reaches the call that never answers");
        host.Clock.Advance(Deadline - OneTick);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        answer.IsCompleted.Should().BeFalse("the deadline has not yet passed");

        host.Clock.Advance(OneTick);
        await Settle.ForAsync(answer, "Tributary answers at its deadline");

        var response = await answer;
        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        (await response.Content.ReadAsStringAsync()).Should().Contain(reason)
            .And.Contain("the platform allows a passed-on request");
        host.Log.Lines.Should().ContainSingle(line => line.Contains("at its deadline") && line.Contains(reason));
        host.Log.ErrorLines.Should().BeEmpty();
        host.BrokerLog.ErrorLines.Should().BeEmpty();
    }

    private sealed class StalledHost : IAsyncDisposable
    {
        private readonly TributaryWebApplicationFactory _factory;
        private readonly IAsyncDisposable _host;
        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeTimeProvider Clock { get; } = new();
        public CapturingLogger<EndpointCallService> Log { get; } = new();
        public CapturingLogger<PassedOnRequestLimit> LimitLog { get; } = new();
        public CapturingLogger<MyceliumClient> BrokerLog { get; } = new();
        public HttpClient Client { get; }
        public Task Stalled => _stalled.Task;

        private StalledHost(
            TributaryWebApplicationFactory factory, string? passedOnRequestLimitSeconds,
            Func<HttpRequestMessage, bool> stalls, Func<HttpRequestMessage, HttpResponseMessage?> answers,
            Task released)
        {
            _factory = factory;
            var handler = MockHttpMessageHandler.ObservingCancellation(async (request, cancellationToken) =>
            {
                if (stalls(request))
                {
                    _stalled.TrySetResult();
                    await released.WaitAsync(cancellationToken);
                }
                return answers(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var host = factory.WithWebHostBuilder(builder =>
            {
                if (passedOnRequestLimitSeconds is not null)
                    builder.UseSetting("PassedOnRequestLimitSeconds", passedOnRequestLimitSeconds);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHttpClientFactory>();
                    services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(handler));
                    services.AddSingleton<TimeProvider>(Clock);
                    services.AddSingleton<ILogger<EndpointCallService>>(Log);
                    services.AddSingleton<ILogger<PassedOnRequestLimit>>(LimitLog);
                    services.AddSingleton<ILogger<MyceliumClient>>(BrokerLog);
                });
            });
            _host = host;
            Client = host.CreateClient();
        }

        public static async Task<StalledHost> StartAsync(
            string? passedOnRequestLimitSeconds,
            Func<HttpRequestMessage, bool> stalls,
            Func<HttpRequestMessage, HttpResponseMessage?> answers,
            Task? released = null)
        {
            var factory = new TributaryWebApplicationFactory();
            await factory.InitializeAsync();
            return new StalledHost(
                factory, passedOnRequestLimitSeconds, stalls, answers,
                released ?? new TaskCompletionSource().Task);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
