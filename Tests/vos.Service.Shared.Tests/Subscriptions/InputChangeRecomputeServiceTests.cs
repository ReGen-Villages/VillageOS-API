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
//
// A daemon is also shared by every project while a subscription belongs to one model, so they pin the
// second half too: each model is followed separately, and every recompute runs under its own model's
// token so a handler writes back where the subject actually lives.
public class InputChangeRecomputeServiceTests
{
    private static readonly Guid Study = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherStudy = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Allocation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SecondAllocation = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ModelOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ModelTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task An_input_change_on_a_watched_subject_recomputes()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne);

        await harness.ClientFor(ModelOne).EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study);
    }

    [Fact]
    public async Task A_change_to_a_property_the_service_writes_does_not_recompute()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne);

        // daysOfSupply is an output. Reacting to it would recompute forever.
        await harness.ClientFor(ModelOne).EmitAsync(Study, "daysOfSupply");
        await harness.ClientFor(ModelOne).EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study, "only the input should have triggered one");
        harness.Recomputes.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_change_on_a_Thing_the_subject_reads_from_recomputes_the_subject()
    {
        // Land allocation reads the programme split off the allocations beside the study, not off the
        // study. Watching only what it wrote would leave a planner's edit changing nothing (#6539).
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne, alsoOn: [Allocation]);

        await harness.ClientFor(ModelOne).EmitAsync(Allocation, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study,
            "the subject is recomputed, never the Thing whose change was noticed");
    }

    [Fact]
    public async Task A_Thing_read_by_the_subject_joins_the_subscription()
    {
        // A change is only delivered for a Thing the subscription covers, so registering the subject
        // alone would leave the follower waiting for an event that never arrives.
        await using var harness = await Harness.StartedAsync();

        await harness.WatchAsync(Study, ModelOne, alsoOn: [Allocation]);

        harness.ClientFor(ModelOne).Members.Should().Contain(new[] { Study, Allocation });
    }

    [Fact]
    public async Task Two_subjects_reading_one_Thing_are_both_recomputed()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne, alsoOn: [Allocation]);
        await harness.WatchAsync(OtherStudy, ModelOne, alsoOn: [Allocation]);

        await harness.ClientFor(ModelOne).EmitAsync(Allocation, "population");

        var recomputed = new[] { (await harness.NextRecomputeAsync()).SubjectId,
                                 (await harness.NextRecomputeAsync()).SubjectId };
        recomputed.Should().BeEquivalentTo(new[] { Study, OtherStudy });
    }

    [Fact]
    public async Task Watching_again_with_a_different_set_stops_following_the_Thing_that_left_it()
    {
        // The set is re-registered on every recompute, because a planner can add or remove an
        // allocation. A Thing that is no longer read must stop recomputing the subject.
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne, alsoOn: [Allocation]);

        await harness.WatchAsync(Study, ModelOne, alsoOn: [SecondAllocation]);

        await harness.ClientFor(ModelOne).EmitAsync(Allocation, "population");
        await harness.ClientFor(ModelOne).EmitAsync(SecondAllocation, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study);
        harness.Recomputes.Should().HaveCount(1, "only the Thing still read should have recomputed it");
    }

    [Fact]
    public async Task A_reconnect_recomputes_each_subject_once_not_each_Thing_it_reads()
    {
        // A derived value is published live-only, so what moved while the stream was down was never
        // replayed. Recomputing per watched Thing would run the same subject once per allocation.
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne, alsoOn: [Allocation, SecondAllocation]);

        harness.ClientFor(ModelOne).RaiseReconnected();

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study);
        harness.Recomputes.Should().HaveCount(1, "one subject, however many Things it reads");
    }

    [Fact]
    public async Task A_change_on_a_subject_the_service_never_computed_for_is_ignored()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne);

        await harness.ClientFor(ModelOne).EmitAsync(Guid.NewGuid(), "population");
        await harness.ClientFor(ModelOne).EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study);
        harness.Recomputes.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_resumed_stream_recomputes_every_watched_subject()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne);

        harness.ClientFor(ModelOne).RaiseReconnected();

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study,
            "a derived value is live-only, so one that moved while the stream was down was never replayed");
    }

    [Fact]
    public async Task A_Thing_read_before_the_subscription_opened_joins_it_too()
    {
        // The opening pass registers what was already watched. Registering only the subjects there
        // would leave a change on an allocation undelivered, and nothing would look wrong.
        var harness = new Harness();
        await harness.WatchAsync(Study, ModelOne, waitForSubscription: false, alsoOn: [Allocation]);

        await harness.StartAsync();
        await harness.WaitForSubscriptionAsync(ModelOne);

        harness.ClientFor(ModelOne).Members.Should().Contain(new[] { Study, Allocation });
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task A_subject_watched_before_the_subscription_opened_is_still_followed()
    {
        var harness = new Harness();
        await harness.WatchAsync(Study, ModelOne, waitForSubscription: false);

        await harness.StartAsync();
        await harness.WaitForSubscriptionAsync(ModelOne);

        harness.ClientFor(ModelOne).Members.Should().Contain(Study);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task A_recompute_that_throws_does_not_stop_later_changes_being_handled()
    {
        await using var harness = await Harness.StartedAsync(failFirstRecompute: true);
        await harness.WatchAsync(Study, ModelOne);

        await harness.ClientFor(ModelOne).EmitAsync(Study, "population");
        await harness.ClientFor(ModelOne).EmitAsync(Study, "storageCapacityM3");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study);
        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study,
            "one failed recompute must not take the stream down with it");
    }

    [Fact]
    public async Task Subscribing_retries_until_mycelium_answers()
    {
        var harness = new Harness { FailSubscribesBefore = 2 };
        await harness.StartAsync();

        await harness.WatchAsync(Study, ModelOne);
        await harness.WaitForSubscriptionAsync(ModelOne);

        harness.ClientFor(ModelOne).SubscribeAttempts.Should().Be(3);
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task Watching_the_same_subject_twice_follows_it_once()
    {
        await using var harness = await Harness.StartedAsync();

        await harness.WatchAsync(Study, ModelOne);
        await harness.WatchAsync(Study, ModelOne);

        await harness.ClientFor(ModelOne).EmitAsync(Study, "population");
        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study);
        harness.ClientFor(ModelOne).Members.Should().ContainSingle();
    }

    // Every other service's tests run in this environment against a synthetic Mycelium URL. Opening a real
    // subscription there would have them all reaching for a socket.
    [Fact]
    public async Task Under_the_Testing_environment_no_subscription_is_opened()
    {
        var harness = new Harness(environmentName: "Testing");

        await harness.Service.StartAsync(CancellationToken.None);
        await harness.WatchAsync(Study, ModelOne);

        harness.Clients.Should().AllSatisfy(client => client.SubscribeAttempts.Should().Be(0));
        await harness.DisposeAsync();
    }

    [Fact]
    public async Task Each_model_is_followed_through_its_own_subscription()
    {
        await using var harness = await Harness.StartedAsync();

        await harness.WatchAsync(Study, ModelOne);
        await harness.WatchAsync(OtherStudy, ModelTwo);

        harness.Clients.Select(client => client.ModelId).Should().BeEquivalentTo(new[] { ModelOne, ModelTwo });
        harness.ClientFor(ModelOne).Members.Should().ContainSingle().Which.Should().Be(Study);
        harness.ClientFor(ModelTwo).Members.Should().ContainSingle().Which.Should().Be(OtherStudy);
    }

    [Fact]
    public async Task A_change_in_a_model_other_than_the_one_that_started_the_daemon_recomputes()
    {
        await using var harness = await Harness.StartedAsync(startupModel: ModelOne);
        await harness.WatchAsync(OtherStudy, ModelTwo);

        await harness.ClientFor(ModelTwo).EmitAsync(OtherStudy, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(OtherStudy,
            "a subject in a second project must be followed, not silently dropped");
    }

    [Fact]
    public async Task A_recompute_runs_under_the_token_of_the_model_the_subject_belongs_to()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(OtherStudy, ModelTwo);

        await harness.ClientFor(ModelTwo).EmitAsync(OtherStudy, "population");

        var recompute = await harness.NextRecomputeAsync();
        ModelScopedBearer.Read(recompute.ActingToken)!.ModelId.Should().Be(ModelTwo,
            "the handler writes its results through the ambient token, so it must name the subject's model");

        // Asserting the model alone is not enough. The stream loop starts inside the /handle call that
        // watched the subject, so it inherits that call's short-lived bearer — which names the same model
        // and would satisfy the check above while the follower's own token went unused. Pinning it to what
        // the exchange issued is what proves the recompute ran under the token the follower holds.
        harness.Exchange.Issued.Should().Contain(recompute.ActingToken);
    }

    [Fact]
    public async Task A_reconnect_recomputes_under_the_token_of_the_model_that_reconnected()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(OtherStudy, ModelTwo);

        harness.ClientFor(ModelTwo).RaiseReconnected();

        var recompute = await harness.NextRecomputeAsync();
        ModelScopedBearer.Read(recompute.ActingToken)!.ModelId.Should().Be(ModelTwo);
    }

    [Fact]
    public async Task A_change_in_one_model_does_not_recompute_a_subject_in_another()
    {
        await using var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne);
        await harness.WatchAsync(OtherStudy, ModelTwo);

        await harness.ClientFor(ModelOne).EmitAsync(OtherStudy, "population");
        await harness.ClientFor(ModelTwo).EmitAsync(OtherStudy, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(OtherStudy);
        harness.Recomputes.Should().HaveCount(1, "the subject is not watched in the model that emitted first");
    }

    [Fact]
    public async Task The_bearer_a_subject_arrived_on_is_traded_for_one_that_outlasts_the_subscription()
    {
        await using var harness = await Harness.StartedAsync();

        await harness.WatchAsync(Study, ModelOne, expiresIn: TimeSpan.FromMinutes(5));

        harness.Exchange.Calls.Should().ContainSingle();
        ModelScopedBearer.Read(harness.Exchange.Calls[0])!.ModelId.Should().Be(ModelOne);
    }

    [Fact]
    public async Task A_bearer_inside_its_replacement_lead_time_is_replaced_before_it_expires()
    {
        await using var harness = await Harness.StartedAsync(
            replacementLeadTime: TimeSpan.FromHours(2), replacementCheckInterval: TimeSpan.FromMilliseconds(20));
        harness.Exchange.IssueExpiringIn = TimeSpan.FromMinutes(30);

        await harness.WatchAsync(Study, ModelOne);

        await harness.Exchange.WaitForCallsAsync(atLeast: 3);
    }

    // Replacing the stored bearer is only half of it. The subscription's own calls — a reconnect, adding
    // a subject — have to start using the replacement, or the follower renews a token it never sends.
    [Fact]
    public async Task A_replaced_bearer_is_what_the_subscriptions_own_calls_then_use()
    {
        await using var harness = await Harness.StartedAsync(
            replacementLeadTime: TimeSpan.FromHours(2), replacementCheckInterval: TimeSpan.FromMilliseconds(20));
        harness.Exchange.IssueExpiringIn = TimeSpan.FromMinutes(30);

        await harness.WatchAsync(Study, ModelOne);
        await harness.Exchange.WaitForCallsAsync(atLeast: 3);

        harness.ClientFor(ModelOne).CurrentToken.Should().Be(harness.Exchange.Issued[^1]);
    }

    // The membership pass that runs as the subscription opens is awaited, so an escaping failure there
    // kills the stream loop before it starts and the whole model stops being followed. The follower logs
    // that the subject's results will go stale and carries on instead.
    //
    // Deliberately the opening pass rather than a later Watch: that one is fire-and-forget, so an
    // escaping exception is swallowed by the unobserved task and proves nothing about the guard.
    [Fact]
    public async Task A_subject_that_cannot_be_added_as_the_subscription_opens_leaves_the_model_still_followed()
    {
        var harness = new Harness();
        await harness.WatchAsync(Study, ModelOne, waitForSubscription: false);
        harness.ClientFor(ModelOne).FailAddObjects = true;

        await harness.StartAsync();
        await harness.WaitForSubscriptionAsync(ModelOne);
        await harness.ClientFor(ModelOne).EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study,
            "a refused membership change must not stop the model being followed");
        await harness.DisposeAsync();
    }

    // One model's unsubscribe failing during shutdown must not stop the others being stopped, so the
    // failure is swallowed. Mycelium is often already gone by the time a daemon gets here.
    [Fact]
    public async Task An_unsubscribe_that_fails_does_not_stop_the_other_models_being_stopped()
    {
        var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne);
        await harness.WatchAsync(OtherStudy, ModelTwo);
        harness.ClientFor(ModelOne).FailUnsubscribe = true;

        var stopping = async () => await harness.DisposeAsync();

        await stopping.Should().NotThrowAsync();
        harness.ClientFor(ModelTwo).Unsubscribed.Should().BeTrue();
    }

    [Fact]
    public async Task Stopping_unsubscribes_every_model_being_followed()
    {
        var harness = await Harness.StartedAsync();
        await harness.WatchAsync(Study, ModelOne);
        await harness.WatchAsync(OtherStudy, ModelTwo);

        await harness.DisposeAsync();

        harness.Clients.Should().AllSatisfy(client => client.Unsubscribed.Should().BeTrue());
    }

    [Fact]
    public async Task A_bearer_with_plenty_of_life_left_is_not_replaced()
    {
        await using var harness = await Harness.StartedAsync(
            replacementLeadTime: TimeSpan.FromHours(2), replacementCheckInterval: TimeSpan.FromMilliseconds(20));
        harness.Exchange.IssueExpiringIn = TimeSpan.FromHours(24);

        await harness.WatchAsync(Study, ModelOne);
        await Task.Delay(150);

        harness.Exchange.Calls.Should().ContainSingle("only the initial trade should have happened");
    }

    [Fact]
    public async Task A_model_whose_token_cannot_be_extended_still_leaves_the_others_running()
    {
        await using var harness = await Harness.StartedAsync();
        harness.Exchange.RefuseFor(ModelTwo);

        await harness.WatchAsync(Study, ModelOne);
        await harness.WatchAsync(OtherStudy, ModelTwo);

        await harness.ClientFor(ModelOne).EmitAsync(Study, "population");

        (await harness.NextRecomputeAsync()).SubjectId.Should().Be(Study,
            "one project's refused token must not take the others down");
        harness.ClientFor(ModelTwo).SubscribeAttempts.Should().BeGreaterThan(0,
            "the refused model keeps following on the bearer it already has");
    }

    [Fact]
    public async Task A_subject_offered_with_no_model_scoped_token_in_hand_is_not_followed()
    {
        await using var harness = await Harness.StartedAsync(startupToken: null);

        harness.Service.Watch(Study);

        harness.Clients.Should().BeEmpty("there is no model to attribute the subject to");
    }

    [Fact]
    public async Task With_no_request_in_hand_the_startup_token_names_the_model()
    {
        await using var harness = await Harness.StartedAsync(startupModel: ModelOne);

        harness.Service.Watch(Study);
        await harness.WaitForSubscriptionAsync(ModelOne);

        harness.ClientFor(ModelOne).Members.Should().Contain(Study);
    }

    [Fact]
    public void AddInputChangeRecompute_resolves_the_service_and_runs_it_as_a_hosted_service()
    {
        using var provider = Registered();

        provider.GetRequiredService<InputChangeRecomputeService>().Should().NotBeNull();
        provider.GetServices<IHostedService>().Should().ContainSingle(s => s is InputChangeRecomputeService);
    }

    [Fact]
    public async Task AddInputChangeRecompute_routes_a_recompute_to_the_registered_handler()
    {
        using var provider = Registered(out var spy);

        // Invoke the wired delegate the way the stream does, without opening a subscription.
        await provider.GetRequiredService<RecomputeInputs>().RecomputeAsync(Study, CancellationToken.None);

        spy.Recorded.Should().ContainSingle().Which.Should().Be(Study);
    }

    private static ServiceProvider Registered() => Registered(out _);

    private static ServiceProvider Registered(out RecomputeSpy spy)
    {
        spy = new RecomputeSpy();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment());
        services.AddSingleton(spy);
        services.AddInputChangeRecompute<RecomputeSpy>(
            "TestCompute", "http://mycelium", serviceToken: null,
            new HashSet<string>(StringComparer.Ordinal) { "population" },
            (handler, subjectId, _) => handler.RecordAsync(subjectId));

        return services.BuildServiceProvider();
    }

    private sealed class RecomputeSpy
    {
        public List<Guid> Recorded { get; } = new();
        public Task RecordAsync(Guid subjectId) { Recorded.Add(subjectId); return Task.CompletedTask; }
    }

    private sealed record Recompute(Guid SubjectId, string? ActingToken);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<Recompute> _recomputes = new();
        private readonly Channel<Recompute> _observed = Channel.CreateUnbounded<Recompute>();
        private readonly List<FakeSubscriptionClient> _clients = new();
        private readonly object _clientsLock = new();

        public FakeTokenExchange Exchange { get; } = new();
        public InputChangeRecomputeService Service { get; }
        public int FailSubscribesBefore { get; init; }

        public IReadOnlyList<Recompute> Recomputes { get { lock (_recomputes) return _recomputes.ToList(); } }
        public IReadOnlyList<FakeSubscriptionClient> Clients { get { lock (_clientsLock) return _clients.ToList(); } }

        public Harness(
            bool failFirstRecompute = false, string environmentName = "Development",
            Guid? startupModel = null, bool noStartupToken = false,
            TimeSpan? replacementLeadTime = null, TimeSpan? replacementCheckInterval = null)
        {
            var failNext = failFirstRecompute;
            var inputs = new RecomputeInputs(
                "TestCompute",
                new HashSet<string>(StringComparer.Ordinal) { "population", "storageCapacityM3" },
                (subjectId, _) =>
                {
                    var observed = new Recompute(subjectId, MyceliumModelToken.Current);
                    lock (_recomputes) _recomputes.Add(observed);
                    _observed.Writer.TryWrite(observed);
                    if (!failNext) return Task.CompletedTask;
                    failNext = false;
                    throw new InvalidOperationException("recompute failed");
                });

            Service = new InputChangeRecomputeService(
                NewClient, Exchange, inputs,
                new FakeEnvironment { EnvironmentName = environmentName }, NullLogger.Instance,
                startupToken: noStartupToken ? null : TestTokens.For(startupModel ?? ModelOne),
                replacementLeadTime, replacementCheckInterval);
        }

        private ISubscriptionClient NewClient(Func<Task<string?>> currentToken)
        {
            var client = new FakeSubscriptionClient(currentToken) { FailSubscribesBefore = FailSubscribesBefore };
            lock (_clientsLock) _clients.Add(client);
            return client;
        }

        public static async Task<Harness> StartedAsync(
            bool failFirstRecompute = false, Guid? startupModel = null, string? startupToken = "present",
            TimeSpan? replacementLeadTime = null, TimeSpan? replacementCheckInterval = null)
        {
            var harness = new Harness(
                failFirstRecompute, startupModel: startupModel, noStartupToken: startupToken is null,
                replacementLeadTime: replacementLeadTime, replacementCheckInterval: replacementCheckInterval);
            await harness.StartAsync();
            return harness;
        }

        public Task StartAsync() => Service.StartAsync(CancellationToken.None);

        /// <summary>Watch a subject the way /handle does: under the bearer that arrived with the call.</summary>
        public async Task WatchAsync(
            Guid subjectId, Guid modelId, TimeSpan? expiresIn = null, bool waitForSubscription = true,
            Guid[]? alsoOn = null)
        {
            var bearer = TestTokens.For(modelId, DateTimeOffset.UtcNow.Add(expiresIn ?? TimeSpan.FromMinutes(5)));
            await MyceliumModelToken.ActingForAsync(bearer, () =>
            {
                Service.Watch(subjectId, alsoOn ?? []);
                return Task.CompletedTask;
            });

            // Watching before the service starts opens no subscription, so waiting for one would only
            // burn the deadline.
            if (waitForSubscription) await WaitForSubscriptionAsync(modelId);
        }

        public async Task WaitForSubscriptionAsync(Guid modelId)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var client = Clients.FirstOrDefault(c => c.ModelId == modelId);
                if (client is not null && client.Subscribed.Task.IsCompleted)
                {
                    // Membership is added right after the subscription opens; let that settle too.
                    await Task.Delay(20);
                    return;
                }
                await Task.Delay(10);
            }
        }

        public FakeSubscriptionClient ClientFor(Guid modelId) =>
            Clients.Single(client => client.ModelId == modelId);

        public async Task<Recompute> NextRecomputeAsync() =>
            await _observed.Reader.ReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        public async ValueTask DisposeAsync() => await Service.StopAsync(CancellationToken.None);
    }

    private sealed class FakeTokenExchange : IServiceTokenExchange
    {
        private readonly List<string> _calls = new();
        private readonly List<string> _issued = new();
        private readonly HashSet<Guid> _refused = new();
        private readonly object _lock = new();

        public TimeSpan IssueExpiringIn { get; set; } = TimeSpan.FromHours(24);
        public IReadOnlyList<string> Calls { get { lock (_lock) return _calls.ToList(); } }
        public IReadOnlyList<string> Issued { get { lock (_lock) return _issued.ToList(); } }

        public void RefuseFor(Guid modelId) { lock (_lock) _refused.Add(modelId); }

        public Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default)
        {
            var model = ModelScopedBearer.Read(bearer)!.ModelId;
            lock (_lock)
            {
                _calls.Add(bearer);
                if (_refused.Contains(model)) return Task.FromResult<ModelScopedBearer?>(null);
            }

            // Distinct from every other issue for the same model, so a test can tell one from the next.
            var extended = TestTokens.For(model, DateTimeOffset.UtcNow.Add(IssueExpiringIn),
                scope: $"endpoint:test:{Guid.NewGuid():N}");
            lock (_lock) _issued.Add(extended);

            return Task.FromResult(ModelScopedBearer.Read(extended));
        }

        public async Task WaitForCallsAsync(int atLeast)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (Calls.Count >= atLeast) return;
                await Task.Delay(10);
            }

            Calls.Count.Should().BeGreaterThanOrEqualTo(atLeast,
                "a bearer inside its lead time should be replaced on each check");
        }
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
        private readonly Func<Task<string?>> _currentToken;
        private long _sequence;

        public FakeSubscriptionClient(Func<Task<string?>> currentToken) => _currentToken = currentToken;

        /// <summary>The model this client speaks for, read off the token it is handed — the same way the
        /// real one learns it. The provider is a completed task, so nothing blocks here.</summary>
        public Guid ModelId => ModelScopedBearer.Read(_currentToken().GetAwaiter().GetResult())!.ModelId;

        /// <summary>What this client would put on its next call. The follower is asked afresh each time,
        /// so a replacement shows up here without the client being rebuilt.</summary>
        public string? CurrentToken => _currentToken().GetAwaiter().GetResult();

        public bool Unsubscribed { get; private set; }

        public TaskCompletionSource Subscribed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FailSubscribesBefore { get; set; }
        public bool FailAddObjects { get; set; }
        public bool FailUnsubscribe { get; set; }
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
            if (FailAddObjects) throw new HttpRequestException("mycelium refused the membership change");

            lock (Members) Members.AddRange(selector.Ids ?? new List<Guid>());
            return Task.FromResult(new AddObjectsResult(0,
                new SnapshotDocument(0, new List<SnapshotThing>(), new List<SnapshotRelationship>())));
        }

        public Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            if (FailUnsubscribe) throw new HttpRequestException("mycelium is already gone");

            Unsubscribed = true;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<ModelChangeEvent> StreamAsync(
            Guid subscriptionId, long fromSequence,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var change in _stream.Reader.ReadAllAsync(ct))
                yield return change;
        }
    }
}
