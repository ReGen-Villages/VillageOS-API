using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Forage.Tests.Helpers;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

public class VocabularyEdgeWriterTests
{
    private const string Source = "flow-survey";

    private readonly ModelBuilder _model = new ModelBuilder()
        .Bound(DiscoveredVocabularyResolver.DiscoveredVocabularyRole, "Vocabulary kind")
        .Archetype("Vocabulary kind")
        .Archetype("FlowRegime")
        .Stating("FlowRegime", DiscoveredVocabularyResolver.ResolvedFromProperty, "flowRegime")
        .Relate("FlowRegime", "is", "Vocabulary kind")
        .Archetype("Spring")
        .Relate("Spring", "flowsAs", "FlowRegime")
        .Relate("steady", "is", "FlowRegime")
        .Relate("flashy", "is", "FlowRegime")
        .Relate("Willow Bend spring", "is", "Spring")
        .Relate("Willow Bend spring", "flowsAs", "flashy");

    private DiscoveryReport SteadyFetched() => new(
        [new SourceOutcome(Source, true, null, SubjectId: _model.Id("Willow Bend spring"),
            Written: new Dictionary<string, string> { ["flowRegime"] = "steady" })],
        []);

    private VocabularyEdgeWriter Writer(
        MockHttpMessageHandler model, ISubscriptionClient? reads = null, ILogger<VocabularyEdgeWriter>? logger = null) => new(
        reads ?? new SnapshotOnly(_model.Build()),
        new MyceliumRelationshipClient(
            new PerCallHttpClientFactory(model), NullLogger<MyceliumRelationshipClient>.Instance,
            "http://localhost:7243", new LaunchToken("test-token")),
        logger ?? NullLogger<VocabularyEdgeWriter>.Instance);

    [Fact]
    public async Task A_write_refused_for_good_is_logged_as_a_warning_naming_the_subject_the_word_and_the_reason()
    {
        var logger = new LevelledLogger();

        await Writer(new MockHttpMessageHandler(_ => EveryWriteAnswered(HttpStatusCode.Forbidden, "not this credential")), logger: logger)
            .ResolveAsync(SteadyFetched(), default);

        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("Willow Bend spring") && entry.Message.Contains("'steady'") && entry.Message.Contains("not this credential"));
    }

    private sealed class LevelledLogger : ILogger<VocabularyEdgeWriter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body = "{}") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage AsAfterAnotherRun(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath switch
        {
            RelationshipWrite.Route => Answer(HttpStatusCode.OK),
            _ when request.Method == HttpMethod.Delete => Answer(HttpStatusCode.NotFound, """{"error":"Relationship not found"}"""),
            _ => Answer(HttpStatusCode.BadRequest,
                """{"error":"A relationship already exists with the same subject, predicate and target."}"""),
        };

    [Fact]
    public async Task A_word_whose_stale_relationship_another_run_already_removed_is_resolved()
    {
        var resolved = await Writer(new MockHttpMessageHandler(AsAfterAnotherRun)).ResolveAsync(SteadyFetched(), default);

        resolved!.NotWritten.Should().BeEmpty();
        resolved.Written.Should().ContainSingle().Which.Word.Should().Be("steady");
    }

    [Fact]
    public async Task The_replacement_is_one_fragment_removing_the_stale_relationship_by_its_three_ends()
    {
        var model = new MockHttpMessageHandler(AsAfterAnotherRun);

        await Writer(model).ResolveAsync(SteadyFetched(), default);

        var write = RelationshipWrite.In(model.Requests.Should().ContainSingle().Subject);
        var (spring, flowsAs) = (_model.Id("Willow Bend spring"), _model.Id("flowsAs"));
        write!.Relationship.Should().Be((spring, flowsAs, _model.Id("steady")));
        write.Replaced.Should().Equal([(spring, flowsAs, _model.Id("flashy"))],
            "a removal named by an identifier another run has already retracted could be refused once it is purged");
    }

    [Fact]
    public async Task A_rejected_replacement_leaves_the_stale_relationship_in_place()
    {
        var model = new MockHttpMessageHandler(request => request.Method == HttpMethod.Delete
            ? Answer(HttpStatusCode.OK)
            : Answer(HttpStatusCode.BadRequest, """{"error":"rejected"}"""));

        var resolved = await Writer(model).ResolveAsync(SteadyFetched(), default);

        resolved!.Faulted.Should().ContainSingle().Which.Edge.Word.Should().Be("steady");
        model.Requests.Should().NotContain(request => request.Method == HttpMethod.Delete,
            "removing the stale relationship on its own would leave the spring with no regime at all");
    }

    private static HttpResponseMessage EveryWriteAnswered(HttpStatusCode status, string error) =>
        Answer(status, $$"""{"error":"{{error}}"}""");

    [Fact]
    public async Task A_write_refused_for_good_is_reported_with_the_platforms_reason_and_is_not_to_be_tried_again()
    {
        const string reason = "This credential may not read everything the fragment names, so nothing was written.";

        var resolved = await Writer(new MockHttpMessageHandler(_ => EveryWriteAnswered(HttpStatusCode.Forbidden, reason)))
            .ResolveAsync(SteadyFetched(), default);

        var refused = resolved!.Refused.Should().ContainSingle().Subject;
        refused.Edge.Word.Should().Be("steady");
        refused.Reason.Should().Be(reason);
        resolved.NotWritten.Should().BeEmpty();
        resolved.Faulted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_write_the_platform_did_not_answer_may_be_tried_again()
    {
        var resolved = await Writer(new MockHttpMessageHandler(_ => EveryWriteAnswered(HttpStatusCode.ServiceUnavailable, "starting")))
            .ResolveAsync(SteadyFetched(), default);

        resolved!.NotWritten.Should().ContainSingle().Which.Word.Should().Be("steady");
        resolved.Refused.Should().BeEmpty();
        resolved.Faulted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rejected_write_whose_relationship_still_resolves_is_this_services_own_fault()
    {
        var resolved = await Writer(new MockHttpMessageHandler(_ => EveryWriteAnswered(HttpStatusCode.BadRequest, "a malformed fragment")))
            .ResolveAsync(SteadyFetched(), default);

        resolved!.Faulted.Should().ContainSingle().Which.Reason.Should().Be("a malformed fragment");
        resolved.NotWritten.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rejected_write_whose_member_was_replaced_since_it_was_read_may_be_tried_again()
    {
        var before = _model.Build();
        var reads = new SnapshotsInTurn(before, WithThingIdChanged(before, _model.Id("steady")));

        var resolved = await Writer(new MockHttpMessageHandler(_ => EveryWriteAnswered(HttpStatusCode.BadRequest, "target does not resolve")), reads)
            .ResolveAsync(SteadyFetched(), default);

        resolved!.NotWritten.Should().ContainSingle().Which.Word.Should().Be("steady");
        resolved.Faulted.Should().BeEmpty();
        reads.Reads.Should().Be(2, "the vocabulary is read again only for the write that was rejected");
    }

    [Fact]
    public async Task A_rejected_write_whose_member_is_gone_moves_the_word_to_the_unknown_words()
    {
        var before = _model.Build();
        var reads = new SnapshotsInTurn(before, WithoutThing(before, _model.Id("steady")));

        var resolved = await Writer(new MockHttpMessageHandler(_ => EveryWriteAnswered(HttpStatusCode.BadRequest, "target does not resolve")), reads)
            .ResolveAsync(SteadyFetched(), default);

        resolved!.Unknown.Should().ContainSingle().Which.Word.Should().Be("steady");
        resolved.NotWritten.Should().BeEmpty();
        resolved.Faulted.Should().BeEmpty();
    }

    private static SnapshotDocument WithThingIdChanged(SnapshotDocument snapshot, Guid from)
    {
        var to = Guid.NewGuid();
        Guid Moved(Guid id) => id == from ? to : id;
        return snapshot with
        {
            Things = [.. snapshot.Things.Select(thing => thing.Id == from ? thing with { Id = to } : thing)],
            Relationships = [.. snapshot.Relationships.Select(edge => edge with { SubjectId = Moved(edge.SubjectId), TargetId = Moved(edge.TargetId) })],
        };
    }

    private static SnapshotDocument WithoutThing(SnapshotDocument snapshot, Guid gone) => snapshot with
    {
        Things = [.. snapshot.Things.Where(thing => thing.Id != gone)],
        Relationships = [.. snapshot.Relationships.Where(edge => edge.SubjectId != gone && edge.TargetId != gone)],
    };

    private sealed class SnapshotsInTurn(SnapshotDocument first, SnapshotDocument afterwards) : SnapshotReader
    {
        public int Reads { get; private set; }

        protected override SnapshotDocument Next() => Reads++ == 0 ? first : afterwards;
    }

    private sealed class SnapshotOnly(SnapshotDocument snapshot) : SnapshotReader
    {
        protected override SnapshotDocument Next() => snapshot;
    }

    private abstract class SnapshotReader : ISubscriptionClient
    {
        protected abstract SnapshotDocument Next();

        public event Action? Reconnected { add { } remove { } }

        public Task<SnapshotReadResult> ReadSnapshotAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
            Task.FromResult(new SnapshotReadResult(0, Next()));

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AddObjectsResult> AddObjectsAsync(Guid subscriptionId, SubscriptionSelector selector, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RemoveObjectsAsync(Guid subscriptionId, IEnumerable<Guid> objectIds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid subscriptionId, long fromSequence, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
