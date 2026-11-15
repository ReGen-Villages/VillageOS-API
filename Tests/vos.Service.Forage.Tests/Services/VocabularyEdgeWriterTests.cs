using System.Net;
using System.Text;
using FluentAssertions;
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

    private VocabularyEdgeWriter Writer(MockHttpMessageHandler model) => new(
        new SnapshotOnly(_model.Build()),
        new MyceliumRelationshipClient(
            new PerCallHttpClientFactory(model), NullLogger<MyceliumRelationshipClient>.Instance,
            "http://localhost:7243", new LaunchToken("test-token")),
        NullLogger<VocabularyEdgeWriter>.Instance);

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
    public async Task A_refused_replacement_leaves_the_stale_relationship_in_place()
    {
        var model = new MockHttpMessageHandler(request => request.Method == HttpMethod.Delete
            ? Answer(HttpStatusCode.OK)
            : Answer(HttpStatusCode.BadRequest, """{"error":"refused"}"""));

        var resolved = await Writer(model).ResolveAsync(SteadyFetched(), default);

        resolved!.NotWritten.Should().ContainSingle().Which.Word.Should().Be("steady");
        model.Requests.Should().NotContain(request => request.Method == HttpMethod.Delete,
            "removing the stale relationship on its own would leave the spring with no regime at all");
    }

    private sealed class SnapshotOnly(SnapshotDocument snapshot) : ISubscriptionClient
    {
        public event Action? Reconnected { add { } remove { } }

        public Task<SnapshotReadResult> ReadSnapshotAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
            Task.FromResult(new SnapshotReadResult(0, snapshot));

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
