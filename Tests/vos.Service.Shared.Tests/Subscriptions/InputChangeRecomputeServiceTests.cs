using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Shared.Tests.Subscriptions;

// A compute service reads its inputs off a study and writes its results back onto the same study, so it
// watches a Thing it also writes to. These pin what that makes delicate: an input change recomputes, an
// output change does not, and a resumed stream recomputes, because a derived value is published live-only
// and a resume never replays one.
public class InputChangeRecomputeServiceTests
{
    private static readonly Guid Study = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task An_input_change_on_a_watched_subject_recomputes()
    {
        await using var harness = await Harness.StartedAsync();
        harness.Service.Watch(Study);

        await harness.Client.EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).Should().Be(Study);
    }

    [Fact]
    public async Task A_change_to_a_property_the_service_writes_does_not_recompute()
    {
        await using var harness = await Harness.StartedAsync();
        harness.Service.Watch(Study);

        // daysOfSupply is an output. Reacting to it would recompute forever.
        await harness.Client.EmitAsync(Study, "daysOfSupply");
        await harness.Client.EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).Should().Be(Study, "only the input should have triggered one");
        harness.Recomputes.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_change_on_a_subject_the_service_never_computed_for_is_ignored()
    {
        await using var harness = await Harness.StartedAsync();
        harness.Service.Watch(Study);

        await harness.Client.EmitAsync(Guid.NewGuid(), "population");
        await harness.Client.EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).Should().Be(Study);
        harness.Recomputes.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_resumed_stream_recomputes_every_watched_subject()
    {
        await using var harness = await Harness.StartedAsync();
        harness.Service.Watch(Study);

        harness.Client.RaiseReconnected();

        (await harness.NextRecomputeAsync()).Should().Be(Study,
            "a derived value is live-only, so one that moved while the stream was down was never replayed");
    }

    [Fact]
    public async Task A_subject_watched_before_the_subscription_opened_is_still_followed()
    {
        var harness = new Harness();
        harness.Service.Watch(Study);

        await harness.StartAsync();

        harness.Client.Members.Should().Contain(Study);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task A_recompute_that_throws_does_not_stop_later_changes_being_handled()
    {
        await using var harness = await Harness.StartedAsync(failFirstRecompute: true);
        harness.Service.Watch(Study);

        await harness.Client.EmitAsync(Study, "population");
        await harness.Client.EmitAsync(Study, "storageCapacityM3");

        (await harness.NextRecomputeAsync()).Should().Be(Study);
        (await harness.NextRecomputeAsync()).Should().Be(Study,
            "one failed recompute must not take the stream down with it");
    }

    [Fact]
    public async Task Subscribing_retries_until_mycelium_answers()
    {
        var harness = new Harness();
        harness.Client.FailSubscribesBefore = 2;

        await harness.StartAsync();

        harness.Client.SubscribeAttempts.Should().Be(3);
        await harness.DisposeAsync();
    }

    [Fact]
    public void AddInputChangeRecompute_resolves_the_service_and_runs_it_as_a_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment());
        services.AddSingleton(new RecomputeSpy());

        services.AddInputChangeRecompute<RecomputeSpy>(
            "TestCompute", "http://mycelium", serviceToken: null,
            new HashSet<string>(StringComparer.Ordinal) { "population" },
            (spy, subjectId, _) => spy.RecordAsync(subjectId));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<InputChangeRecomputeService>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Should().ContainSingle(s => s is InputChangeRecomputeService);
    }

    [Fact]
    public async Task AddInputChangeRecompute_routes_a_recompute_to_the_registered_handler()
    {
        var spy = new RecomputeSpy();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment());
        services.AddSingleton(spy);
        services.AddInputChangeRecompute<RecomputeSpy>(
            "TestCompute", "http://mycelium", serviceToken: null,
            new HashSet<string>(StringComparer.Ordinal) { "population" },
            (handler, subjectId, _) => handler.RecordAsync(subjectId));

        using var provider = services.BuildServiceProvider();

        // Invoke the wired delegate the way the stream does, without opening a subscription.
        await provider.GetRequiredService<RecomputeInputs>().RecomputeAsync(Study, CancellationToken.None);

        spy.Recorded.Should().ContainSingle().Which.Should().Be(Study);
    }

    [Fact]
    public async Task Watching_the_same_subject_twice_follows_it_once()
    {
        await using var harness = await Harness.StartedAsync();

        harness.Service.Watch(Study);
        harness.Service.Watch(Study);

        await harness.Client.EmitAsync(Study, "population");
        (await harness.NextRecomputeAsync()).Should().Be(Study);
        harness.Client.Members.Should().ContainSingle();
    }

    // Every other service's tests run in this environment against a synthetic Mycelium URL. Opening a real
    // subscription there would have them all reaching for a socket.
    [Fact]
    public async Task Under_the_Testing_environment_no_subscription_is_opened()
    {
        var harness = new Harness(environmentName: "Testing");

        await harness.Service.StartAsync(CancellationToken.None);

        harness.Client.SubscribeAttempts.Should().Be(0);
        await harness.DisposeAsync();
    }

    private sealed class RecomputeSpy
    {
        public List<Guid> Recorded { get; } = new();
        public Task RecordAsync(Guid subjectId) { Recorded.Add(subjectId); return Task.CompletedTask; }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<Guid> _recomputes = new();
        private readonly Channel<Guid> _observed = Channel.CreateUnbounded<Guid>();

        public FakeSubscriptionClient Client { get; } = new();
        public InputChangeRecomputeService Service { get; }
        public IReadOnlyList<Guid> Recomputes { get { lock (_recomputes) return _recomputes.ToList(); } }

        public Harness(bool failFirstRecompute = false, string environmentName = "Development")
        {
            var failNext = failFirstRecompute;
            var inputs = new RecomputeInputs(
                "TestCompute",
                new HashSet<string>(StringComparer.Ordinal) { "population", "storageCapacityM3" },
                (subjectId, _) =>
                {
                    lock (_recomputes) _recomputes.Add(subjectId);
                    _observed.Writer.TryWrite(subjectId);
                    if (!failNext) return Task.CompletedTask;
                    failNext = false;
                    throw new InvalidOperationException("recompute failed");
                });

            Service = new InputChangeRecomputeService(
                Client, inputs, new FakeEnvironment { EnvironmentName = environmentName }, NullLogger.Instance);
        }

        public static async Task<Harness> StartedAsync(bool failFirstRecompute = false)
        {
            var harness = new Harness(failFirstRecompute);
            await harness.StartAsync();
            return harness;
        }

        public async Task StartAsync()
        {
            await Service.StartAsync(CancellationToken.None);
            await Client.Subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async Task<Guid> NextRecomputeAsync() =>
            await _observed.Reader.ReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        public async ValueTask DisposeAsync() => await Service.StopAsync(CancellationToken.None);
    }

    // Not the "Testing" environment: these tests exercise the subscription itself, which StartAsync skips there.
    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class FakeSubscriptionClient : ISubscriptionClient
    {
        private readonly Channel<ModelChangeEvent> _stream = Channel.CreateUnbounded<ModelChangeEvent>();
        private long _sequence;

        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FailSubscribesBefore { get; set; }
        public int SubscribeAttempts { get; private set; }
        public List<Guid> Members { get; } = new();
        public event Action? Reconnected;

        public void RaiseReconnected() => Reconnected?.Invoke();

        public Task EmitAsync(Guid entityId, string propertyName) =>
            _stream.Writer.WriteAsync(new ModelChangeEvent
            {
                Sequence = Interlocked.Increment(ref _sequence),
                Kind = "PropertyChanged",
                EntityId = entityId,
                PropertyName = propertyName,
                Value = JsonDocument.Parse("1").RootElement,
            }).AsTask();

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default)
        {
            SubscribeAttempts++;
            if (SubscribeAttempts <= FailSubscribesBefore)
                throw new HttpRequestException("mycelium not up yet");

            Subscribed.TrySetResult();
            return Task.FromResult(new SubscribeResult(Guid.NewGuid(), 0,
                new SnapshotDocument(0, new List<SnapshotThing>(), new List<SnapshotRelationship>())));
        }

        public Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default)
        {
            lock (Members) Members.AddRange(selector.Ids ?? new List<Guid>());
            return Task.FromResult(new AddObjectsResult(0,
                new SnapshotDocument(0, new List<SnapshotThing>(), new List<SnapshotRelationship>())));
        }

        public Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default) => Task.CompletedTask;

        public async IAsyncEnumerable<ModelChangeEvent> StreamAsync(
            Guid subscriptionId, long fromSequence,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var change in _stream.Reader.ReadAllAsync(ct))
                yield return change;
        }
    }
}
