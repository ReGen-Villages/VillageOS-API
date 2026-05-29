using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using vos.ManagedMicroservice.Metabolism.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

/// <summary>
/// Task #5457: ConnectSignalRAsync builds its hub through <see cref="IHubConnectionFactory"/>
/// and routes hub callbacks. A mocked <see cref="IHubConnection"/> stands in for the real
/// SignalR connection — previously unreachable from a unit test — and a recording
/// <c>DelayAsync</c> seam (a protected virtual on the class under test, so it can't be mocked)
/// lets the retry cadence be asserted without real sleeps.
/// </summary>
public class BrokerClientConnectSignalRTests
{
    private static readonly int[] ExpectedBackoff = { 0, 1000, 2000, 5000, 10000 };

    [Fact]
    public async Task ConnectSignalRAsync_TokenFetchKeepsFailing_FollowsDocumentedBackoffSequence()
    {
        // Token endpoint always 500 → GetTokenAsync returns null every attempt → retry path.
        var factory = new Mock<IHubConnectionFactory>();
        var client = NewClient(factory, TokenAlwaysFailsMock());
        using var cts = new CancellationTokenSource();
        client.Cts = cts;
        client.CancelAfterDelays = 6; // capture the five documented values + one capped repeat

        await client.ConnectSignalRAsync(cts.Token);

        client.RecordedDelays.Take(5).Should().Equal(ExpectedBackoff);
        client.RecordedDelays[5].Should().Be(10000, "the final backoff value is held, not exceeded, past the table");
        factory.Verify(f => f.Create(It.IsAny<string>(), It.IsAny<Func<Task<string?>>>()), Times.Never);
    }

    [Fact]
    public async Task ConnectSignalRAsync_CancelledMidRetry_StopsWithoutFurtherAttempts()
    {
        var factory = new Mock<IHubConnectionFactory>();
        var client = NewClient(factory, TokenAlwaysFailsMock());
        using var cts = new CancellationTokenSource();
        client.Cts = cts;
        client.CancelAfterDelays = 1; // cancel right after the first backoff

        await client.ConnectSignalRAsync(cts.Token);

        client.RecordedDelays.Should().HaveCount(1, "the loop must exit on the next iteration after cancellation");
        factory.Verify(f => f.Create(It.IsAny<string>(), It.IsAny<Func<Task<string?>>>()), Times.Never,
            "no hub is built while the token is unavailable");
    }

    [Fact]
    public async Task ConnectSignalRAsync_TokenUnavailableThenAvailable_RefetchesTokenEachAttempt()
    {
        // First token call 500, second returns a token. Connect must re-fetch, not reuse the failure.
        var tokenCalls = 0;
        var mock = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token")
            {
                tokenCalls++;
                return tokenCalls == 1
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : JsonOk("{\"token\":\"fake-jwt\"}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var (factory, hub) = NewFactoryWithHub();
        var client = NewClient(factory, mock);

        await client.ConnectSignalRAsync();

        tokenCalls.Should().Be(2, "the token is re-fetched on the retry rather than cached from the failed attempt");
        factory.Verify(f => f.Create(It.IsAny<string>(), It.IsAny<Func<Task<string?>>>()), Times.Once,
            "the hub is built once, only after a token is obtained");
        hub.Verify(h => h.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectSignalRAsync_StartFailsThenSucceeds_RetriesAndConnects()
    {
        var (factory, hub) = NewFactoryWithHub();
        hub.SetupSequence(h => h.StartAsync(It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("simulated StartAsync failure")) // first attempt
            .Returns(Task.CompletedTask);                                          // retry succeeds
        var client = NewClient(factory, TokenAlwaysOkMock());

        await client.ConnectSignalRAsync();

        factory.Verify(f => f.Create(It.IsAny<string>(), It.IsAny<Func<Task<string?>>>()), Times.Exactly(2),
            "a fresh connection is built on the retry after StartAsync throws");
        hub.Verify(h => h.StartAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ConnectSignalRAsync_RelationshipPropertyChangedFromHub_RoutesThroughRaiseWithRightShape()
    {
        var (factory, hub) = NewFactoryWithHub();
        Action<Guid, string, object?>? registered = null;
        hub.Setup(h => h.On<Guid, string, object?>("RelationshipPropertyChanged", It.IsAny<Action<Guid, string, object?>>()))
            .Callback<string, Action<Guid, string, object?>>((_, handler) => registered = handler);
        var client = NewClient(factory, TokenAlwaysOkMock());
        await client.ConnectSignalRAsync();

        (Guid Id, string Prop, object? Value) captured = (Guid.Empty, "", null);
        client.OnRelationshipPropertyChanged += (id, prop, value) => captured = (id, prop, value);

        registered.Should().NotBeNull("ConnectSignalRAsync must register the RelationshipPropertyChanged handler on the hub");
        var relId = Guid.NewGuid();
        registered!.Invoke(relId, "amount", 5.0m); // the hub would invoke this on an inbound event

        captured.Id.Should().Be(relId);
        captured.Prop.Should().Be("amount");
        captured.Value.Should().Be(5.0m);
    }

    [Fact]
    public async Task ConnectSignalRAsync_Reconnected_LogsNewConnectionIdWithoutThrowing()
    {
        var (factory, hub) = NewFactoryWithHub();
        var logger = new RecordingLogger<BrokerClient>();
        var client = NewClient(factory, TokenAlwaysOkMock(), logger);
        await client.ConnectSignalRAsync();

        var raise = () => hub.Raise(h => h.Reconnected += null, "conn-xyz");

        raise.Should().NotThrow();
        logger.Messages.Should().Contain(m => m.Contains("reconnected") && m.Contains("conn-xyz"));
    }

    // ---- helpers ----

    private static MockHttpMessageHandler TokenAlwaysFailsMock() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

    private static MockHttpMessageHandler TokenAlwaysOkMock() =>
        new(req => req.RequestUri!.AbsolutePath == "/api/auth/token"
            ? JsonOk("{\"token\":\"fake-jwt\"}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

    private static HttpResponseMessage JsonOk(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>A mocked hub wired into a mocked factory. StartAsync returns a completed task by Moq default.</summary>
    private static (Mock<IHubConnectionFactory> factory, Mock<IHubConnection> hub) NewFactoryWithHub()
    {
        var hub = new Mock<IHubConnection>();
        var factory = new Mock<IHubConnectionFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<Func<Task<string?>>>())).Returns(hub.Object);
        return (factory, hub);
    }

    private static RecordingBrokerClient NewClient(
        Mock<IHubConnectionFactory> factory, MockHttpMessageHandler mock, ILogger<BrokerClient>? logger = null)
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(mock, disposeHandler: false) { BaseAddress = new Uri("http://test-broker") });
        return new RecordingBrokerClient(
            httpFactory.Object,
            logger ?? Mock.Of<ILogger<BrokerClient>>(),
            "http://test-broker", "consumes", factory.Object);
    }

    /// <summary>BrokerClient with a synchronous, recording <c>DelayAsync</c> so retry timing is observable and instant.</summary>
    private sealed class RecordingBrokerClient : BrokerClient
    {
        public RecordingBrokerClient(IHttpClientFactory http, ILogger<BrokerClient> log, string url, string mode, IHubConnectionFactory factory)
            : base(http, log, url, mode, serviceToken: null, hubFactory: factory) { }

        public List<int> RecordedDelays { get; } = new();
        public CancellationTokenSource? Cts { get; set; }
        public int CancelAfterDelays { get; set; } = int.MaxValue;

        protected override Task DelayAsync(int milliseconds, CancellationToken ct)
        {
            RecordedDelays.Add(milliseconds);
            if (RecordedDelays.Count >= CancelAfterDelays) Cts?.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public readonly List<string> Messages = new();
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, ex));
    }
}
