using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.Subscriptions;

// A subscription lives in the broker's memory, so a broker that restarts answers 404 for the one a follower
// holds. The follower runs here over the real client, because the client is what learns that the subscription
// is gone and the follower is what opens another.
public sealed class FollowingAfterTheBrokerRestartsTests : IAsyncLifetime
{
    private static readonly Guid Model = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Study = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherStudy = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Allocation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid FormerKind = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid PresentKind = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private readonly Broker _broker = new();
    private readonly List<(Guid Subject, Guid? ActingIn)> _recomputes = new();
    private readonly InputChangeRecomputeService _service;

    public FollowingAfterTheBrokerRestartsTests()
    {
        var inputs = new RecomputeInputs(
            "TestCompute",
            () => new HashSet<string>(StringComparer.Ordinal) { "population" },
            (subject, _) =>
            {
                lock (_recomputes) _recomputes.Add((subject, ModelScopedBearer.Read(MyceliumModelToken.Current)?.ModelId));
                return Task.CompletedTask;
            });
        _service = new InputChangeRecomputeService(
            currentToken => new SubscriptionClient(
                new PerCallHttpClientFactory(_broker), NullLogger.Instance, "http://broker", tokenProvider: currentToken)
            {
                ReconnectDelay = TimeSpan.FromMilliseconds(5),
            },
            new ExchangesNothing(), inputs, new DevelopmentEnvironment(), NullLogger.Instance);
    }

    public Task InitializeAsync() => _service.StartAsync(CancellationToken.None);

    public async Task DisposeAsync() => await _service.StopAsync(CancellationToken.None);

    [Fact]
    public async Task Every_watched_Thing_is_added_to_a_new_subscription()
    {
        await WatchAsync(Study, Allocation);

        _broker.Restart();

        await FollowsOnANewSubscriptionAsync(Study, Allocation);
    }

    [Fact]
    public async Task Each_subject_is_recomputed_once_under_its_models_token()
    {
        await WatchAsync(Study, Allocation);
        await WatchAsync(OtherStudy);

        _broker.Restart();

        await Settle.UntilAsync(() => Recomputes.Count >= 2, "both subjects are recomputed once the new stream opens");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        Recomputes.Should().BeEquivalentTo(new[] { (Study, (Guid?)Model), (OtherStudy, (Guid?)Model) });
    }

    [Fact]
    public async Task The_recompute_waits_for_the_new_stream_to_open()
    {
        await WatchAsync(Study);
        _broker.HoldStreams();

        _broker.Restart();
        await _broker.UntilAStreamIsHeldAsync();
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        Recomputes.Should().BeEmpty("a derived value published before the stream opens would never reach a recompute already made");

        _broker.ReleaseStreams();
        await Settle.UntilAsync(() => Recomputes.Count == 1, "the subject is recomputed once the stream is open");
    }

    [Fact]
    public async Task A_change_on_the_new_subscription_recomputes_its_subject()
    {
        await WatchAsync(Study);
        _broker.Restart();
        await FollowsOnANewSubscriptionAsync(Study);
        await RecomputedAsync(Study, times: 1);

        await _broker.EmitAsync(Study, "population");

        await RecomputedAsync(Study, times: 2);
    }

    [Fact]
    public async Task A_type_changed_while_the_broker_was_away_replaces_the_one_held_before()
    {
        _broker.TypeOf[Study] = FormerKind;
        await WatchAsync(Study);

        _broker.TypeOf[Study] = PresentKind;
        _broker.Restart();
        await FollowsOnANewSubscriptionAsync(Study, PresentKind);
        await RecomputedAsync(Study, times: 1);

        await _broker.EmitAsync(FormerKind, "population");
        await _broker.EmitAsync(PresentKind, "population");

        await RecomputedAsync(Study, times: 2);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        Recomputes.Should().HaveCount(2, "an edit on the kind the study no longer is moves nothing it reads");
    }

    [Fact]
    public async Task An_add_still_under_way_when_the_subscription_is_lost_leaves_the_Thing_on_the_new_one_with_its_present_type()
    {
        await WatchAsync(OtherStudy);
        _broker.TypeOf[Study] = FormerKind;
        _broker.HoldTheNextAdd();
        Watch(Study);
        await _broker.UntilAnAddIsHeldAsync();

        _broker.TypeOf[Study] = PresentKind;
        _broker.Restart();
        await FollowsOnANewSubscriptionAsync(OtherStudy, Study, PresentKind);
        await RecomputedAsync(Study, times: 1);
        _broker.ReleaseTheAdd();
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));

        await _broker.EmitAsync(FormerKind, "population");
        await _broker.EmitAsync(PresentKind, "population");

        await RecomputedAsync(Study, times: 2);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        Recomputes.Count(recompute => recompute.Subject == Study).Should().Be(2,
            "the late answer described the subscription that is gone");
    }

    [Fact]
    public async Task A_subject_first_watched_while_the_broker_is_away_joins_the_new_subscription()
    {
        await WatchAsync(Study);

        _broker.GoAway();
        Watch(OtherStudy);
        _broker.ComeBack();

        await FollowsOnANewSubscriptionAsync(Study, OtherStudy);
    }

    [Fact]
    public async Task A_subscribe_that_times_out_is_tried_again()
    {
        await WatchAsync(Study);
        _broker.TimeOutTheNextSubscribe();

        _broker.Restart();

        await FollowsOnANewSubscriptionAsync(Study);
    }

    [Fact]
    public async Task Stopping_while_the_broker_is_away_opens_nothing_when_it_comes_back()
    {
        await WatchAsync(Study);
        _broker.GoAway();

        await _service.StopAsync(CancellationToken.None);
        _broker.ComeBack();
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));

        _broker.Opened.Should().HaveCount(1);
    }

    [Fact]
    public async Task Stopping_unsubscribes_the_newest_subscription()
    {
        await WatchAsync(Study);
        _broker.Restart();
        await FollowsOnANewSubscriptionAsync(Study);

        await _service.StopAsync(CancellationToken.None);

        _broker.Unsubscribed.Should().Contain(_broker.Opened[^1]);
    }

    [Fact]
    public async Task A_position_the_broker_no_longer_keeps_opens_one_new_subscription_and_recomputes_once()
    {
        await WatchAsync(Study, Allocation);

        _broker.ForgetEveryHeldPosition();

        await FollowsOnANewSubscriptionAsync(Study, Allocation);
        await RecomputedAsync(Study, times: 1);
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        Recomputes.Should().ContainSingle();
        _broker.Opened.Should().HaveCount(2);
        _broker.Unsubscribed.Should().Equal(_broker.Opened[0]);
    }

    private IReadOnlyList<(Guid Subject, Guid? ActingIn)> Recomputes
    {
        get { lock (_recomputes) return _recomputes.ToList(); }
    }

    private void Watch(Guid subject, params Guid[] readsFrom) =>
        MyceliumModelToken.ActingForAsync(TestTokens.For(Model), () =>
        {
            _service.Watch(subject, readsFrom);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private async Task WatchAsync(Guid subject, params Guid[] readsFrom)
    {
        Watch(subject, readsFrom);
        await Settle.UntilAsync(
            () => _broker.StreamIsOpen && _broker.Members.IsSupersetOf([subject, .. readsFrom]),
            "the subject is followed and the stream is open");
    }

    private Task FollowsOnANewSubscriptionAsync(params Guid[] things) =>
        Settle.UntilAsync(
            () => _broker.Opened.Count > 1 && _broker.StreamIsOpen && _broker.Members.IsSupersetOf(things),
            "a new subscription holds every watched Thing and its stream is open");

    private Task RecomputedAsync(Guid subject, int times) =>
        Settle.UntilAsync(() => Recomputes.Count(recompute => recompute.Subject == subject) >= times, $"{subject} is recomputed");

    private sealed class ExchangesNothing : IServiceTokenExchange
    {
        public Task<ModelScopedBearer?> ExchangeAsync(string bearer, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModelScopedBearer?>(null);
    }

    // Named anything but Testing, under which no subscription is opened.
    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // The broker's subscription routes over a registry that a restart empties.
    private sealed class Broker : HttpMessageHandler
    {
        private sealed class Subscription
        {
            public HashSet<Guid> Members { get; } = new();
            public Pipe? Stream { get; set; }
        }

        private readonly Lock _lock = new();
        private readonly Dictionary<Guid, Subscription> _held = new();
        private readonly List<Guid> _opened = new();
        private readonly List<Guid> _unsubscribed = new();
        private bool _away;
        private bool _nextSubscribeTimesOut;
        private bool _nextAddIsHeld;
        private bool _streamsAreHeld;
        private readonly TaskCompletionSource _addHeld = NewSignal();
        private readonly TaskCompletionSource _addReleased = NewSignal();
        private readonly TaskCompletionSource _streamHeld = NewSignal();
        private readonly TaskCompletionSource _streamsReleased = NewSignal();

        public ConcurrentDictionary<Guid, Guid> TypeOf { get; } = new();
        public IReadOnlyList<Guid> Opened { get { lock (_lock) return _opened.ToList(); } }
        public IReadOnlyList<Guid> Unsubscribed { get { lock (_lock) return _unsubscribed.ToList(); } }
        public HashSet<Guid> Members { get { lock (_lock) return Newest is { } newest ? [.. newest.Members] : []; } }
        public bool StreamIsOpen { get { lock (_lock) return Newest?.Stream is not null; } }

        private Subscription? Newest => _opened.Count > 0 ? _held.GetValueOrDefault(_opened[^1]) : null;

        public void Restart()
        {
            List<Pipe> streams;
            lock (_lock)
            {
                streams = _held.Values.Where(held => held.Stream is not null).Select(held => held.Stream!).ToList();
                _held.Clear();
            }
            foreach (var stream in streams) stream.Writer.Complete();
        }

        private const string SnapshotRequired =
            "event: SnapshotRequired\ndata: {\"resumeAfter\":0,\"reason\":\"resume position precedes the retained commit-log floor\"}\n\n";

        private readonly HashSet<Guid> _behind = new();

        // As the broker answers a subscription once compaction has removed records it was not yet sent.
        public void ForgetEveryHeldPosition()
        {
            List<Pipe> streams;
            lock (_lock)
            {
                _behind.UnionWith(_held.Keys);
                streams = _held.Values.Where(held => held.Stream is not null).Select(held => held.Stream!).ToList();
            }
            foreach (var stream in streams)
            {
                stream.Writer.WriteAsync(Encoding.UTF8.GetBytes(SnapshotRequired)).AsTask().GetAwaiter().GetResult();
                stream.Writer.Complete();
            }
        }

        public void GoAway()
        {
            lock (_lock) _away = true;
            Restart();
        }

        public void ComeBack() { lock (_lock) _away = false; }

        public void TimeOutTheNextSubscribe() { lock (_lock) _nextSubscribeTimesOut = true; }

        public void HoldTheNextAdd() { lock (_lock) _nextAddIsHeld = true; }
        public Task UntilAnAddIsHeldAsync() => Settle.ForAsync(_addHeld.Task, "the add reaches the broker");
        public void ReleaseTheAdd() => _addReleased.SetResult();

        public void HoldStreams() { lock (_lock) _streamsAreHeld = true; }
        public Task UntilAStreamIsHeldAsync() => Settle.ForAsync(_streamHeld.Task, "a stream is asked for");
        public void ReleaseStreams() => _streamsReleased.SetResult();

        public async Task EmitAsync(Guid thing, string property)
        {
            Pipe stream;
            lock (_lock) stream = Newest!.Stream!;
            var frame = $"id: {Interlocked.Increment(ref _sequence)}\nevent: PropertyChanged\n"
                + $"data: {{\"EntityId\":\"{thing}\",\"PropertyName\":\"{property}\",\"Value\":1}}\n\n";
            await stream.Writer.WriteAsync(Encoding.UTF8.GetBytes(frame));
        }

        private long _sequence;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_lock)
                if (_away) throw new HttpRequestException("the broker is away");

            var path = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            if (path.Length == 2) return OpenSubscription();

            var id = Guid.Parse(path[2]);
            if (path.Length == 3) return Unsubscribe(id);
            if (path[3] == "objects") return await AddAsync(id, request, cancellationToken);
            return await OpenStreamAsync(id, cancellationToken);
        }

        private HttpResponseMessage OpenSubscription()
        {
            var id = Guid.NewGuid();
            lock (_lock)
            {
                if (_nextSubscribeTimesOut)
                {
                    _nextSubscribeTimesOut = false;
                    throw new TaskCanceledException("the subscribe timed out");
                }
                _held[id] = new Subscription();
                _opened.Add(id);
            }
            return Answer(new { subscriptionId = id, watermark = 0, snapshot = SnapshotOf([]) });
        }

        private HttpResponseMessage Unsubscribe(Guid id)
        {
            lock (_lock)
            {
                _unsubscribed.Add(id);
                return new HttpResponseMessage(_held.Remove(id) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
            }
        }

        private async Task<HttpResponseMessage> AddAsync(Guid id, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var things = body.GetProperty("ids").EnumerateArray().Select(named => named.GetGuid()).ToList();
            bool held;
            HttpResponseMessage answer;
            lock (_lock)
            {
                held = _nextAddIsHeld;
                _nextAddIsHeld = false;
                if (!_held.TryGetValue(id, out var subscription))
                {
                    answer = new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                else
                {
                    things.AddRange(things.Where(TypeOf.ContainsKey).Select(thing => TypeOf[thing]).ToList());
                    subscription.Members.UnionWith(things);
                    answer = Answer(new { watermark = 0, snapshot = SnapshotOf(things) });
                }
            }
            if (held)
            {
                _addHeld.SetResult();
                await _addReleased.Task.WaitAsync(cancellationToken);
            }
            return answer;
        }

        private async Task<HttpResponseMessage> OpenStreamAsync(Guid id, CancellationToken cancellationToken)
        {
            var pipe = new Pipe();
            bool held;
            lock (_lock)
            {
                if (!_held.TryGetValue(id, out var subscription)) return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (_behind.Contains(id))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SnapshotRequired) };
                subscription.Stream = pipe;
                held = _streamsAreHeld;
            }
            if (held)
            {
                _streamHeld.TrySetResult();
                await _streamsReleased.Task.WaitAsync(cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(pipe.Reader.AsStream()) };
        }

        private static object SnapshotOf(IEnumerable<Guid> things) => new
        {
            watermark = 0,
            things = things.Select(thing => new
            {
                id = thing, name = (string?)null, isArchetype = false,
                properties = new { }, states = Array.Empty<string>(), relationships = Array.Empty<Guid>(),
            }),
            relationships = Array.Empty<object>(),
        };

        private static HttpResponseMessage Answer(object body) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
