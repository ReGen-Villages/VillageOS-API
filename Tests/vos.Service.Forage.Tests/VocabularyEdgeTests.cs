using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

// A discovered word becomes the edge the model declares, inside the run that fetched it (#6809; the
// declaration is platform User Story 6773). Driven through /handle because the thing worth pinning is
// the whole path: the fetch response reports what was written, the declaration is read back by mark,
// and the edge lands — with the stale one removed first — before the analysis starts. Nothing here
// tells Forage the vocabulary's names; the model declares them and the run reads them.
public class VocabularyEdgeTests
{
    private const string FetchRoute = "/api/endpoints/tributary";

    private static Dictionary<string, Guid> Names(params string[] names) =>
        names.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, Guid> CatchmentWithOneSource() => Names(
        "WillowBend", "Portugal", "isIn", "covers", "resolvedBy", "is",
        "FlowSurvey", "FlowSurveyEndpoint",
        "Catchment", "FlowRegime", "flowsAs", "steady", "flashy");

    private static Archetype[] DeclaredArchetypes() =>
    [
        new("Catchment"),
        new("FlowRegime", DiscoveredVocabularyResolver.ArchetypeFlag, new Dictionary<string, string>
        {
            [DiscoveredVocabularyResolver.ResolvedFromProperty] = "\"flowRegime\"",
        }),
    ];

    private static Edge[] DeclaredModel(params Edge[] extraEdges) =>
    [
        new Edge("WillowBend", "isIn", "Portugal"),
        new Edge("FlowSurvey", "covers", "Portugal"),
        new Edge("FlowSurvey", "resolvedBy", "FlowSurveyEndpoint"),
        new Edge("WillowBend", "is", "Catchment"),
        new Edge("Catchment", "flowsAs", "FlowRegime"),
        new Edge("steady", "is", "FlowRegime"),
        new Edge("flashy", "is", "FlowRegime"),
        .. extraEdges,
    ];

    private sealed class EdgeRecorder
    {
        public List<(Guid Subject, Guid Predicate, Guid Target)> Written { get; } = new();
        public List<Guid> Deleted { get; } = new();

        public HttpResponseMessage? Route(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Delete
                && path.StartsWith("/api/relationships/", StringComparison.Ordinal))
            {
                Deleted.Add(Guid.Parse(path["/api/relationships/".Length..]));
                return Ok("{}");
            }

            if (request.Method != HttpMethod.Post || path != "/api/relationships")
                return null;

            var body = JsonDocument.Parse(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
            Written.Add((
                body.GetProperty("subjectId").GetGuid(),
                body.GetProperty("predicateId").GetGuid(),
                body.GetProperty("targetId").GetGuid()));
            return Ok("{}");
        }
    }

    private static async Task<HttpResponseMessage> RunDiscovery(
        Dictionary<string, Guid> ids, Edge[] edges, EdgeRecorder recorder, string fetchAnswer)
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
            recorder.Route(request)
            ?? (request.RequestUri!.AbsolutePath == FetchRoute ? Ok(fetchAnswer) : null)
            ?? RouteSubscription(request, ids, edges, siteValues: null, DeclaredArchetypes())
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();
        return response;
    }

    [Fact]
    public async Task A_fetched_word_lands_as_the_edge_the_model_declares()
    {
        var ids = CatchmentWithOneSource();
        var recorder = new EdgeRecorder();

        var response = await RunDiscovery(ids, DeclaredModel(), recorder,
            """{"success":true,"written":{"flowRegime":"steady"}}""");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        recorder.Written.Should().ContainSingle()
            .Which.Should().Be((ids["WillowBend"], ids["flowsAs"], ids["steady"]));
        recorder.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_changed_word_removes_the_stale_edge_before_writing_its_replacement()
    {
        var ids = CatchmentWithOneSource();
        var stale = Guid.NewGuid();
        var recorder = new EdgeRecorder();

        await RunDiscovery(ids, DeclaredModel(new Edge("WillowBend", "flowsAs", "flashy", stale)),
            recorder, """{"success":true,"written":{"flowRegime":"steady"}}""");

        recorder.Deleted.Should().Equal(stale);
        recorder.Written.Should().ContainSingle()
            .Which.Should().Be((ids["WillowBend"], ids["flowsAs"], ids["steady"]));
    }

    // The stated rule for a word outside the vocabulary: the observation stays, no edge is written,
    // and the run reports it — neither silently dropped nor silently written.
    [Fact]
    public async Task A_word_the_vocabulary_does_not_hold_writes_no_edge()
    {
        var ids = CatchmentWithOneSource();
        var recorder = new EdgeRecorder();

        await RunDiscovery(ids, DeclaredModel(), recorder,
            """{"success":true,"written":{"flowRegime":"torrential"}}""");

        recorder.Written.Should().BeEmpty();
        recorder.Deleted.Should().BeEmpty();
    }

    // A fetch that wrote nothing — a provider hedge, a coordinate it cannot classify — resolves
    // nothing, and an older fetching service that reports no writes resolves nothing rather than
    // failing the run.
    [Fact]
    public async Task A_fetch_that_reports_no_writes_resolves_nothing()
    {
        var ids = CatchmentWithOneSource();
        var recorder = new EdgeRecorder();

        await RunDiscovery(ids, DeclaredModel(), recorder, """{"success":true,"written":{}}""");

        recorder.Written.Should().BeEmpty();
    }

    // Writing beside an edge that would not go would leave a reader two answers; leaving the stale one
    // alone keeps exactly one, and the next run resolves again.
    [Fact]
    public async Task A_stale_edge_that_will_not_go_blocks_its_replacement()
    {
        var ids = CatchmentWithOneSource();
        var stale = Guid.NewGuid();
        var recorder = new EdgeRecorder();

        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.Method == HttpMethod.Delete
                && request.RequestUri!.AbsolutePath.StartsWith("/api/relationships/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return recorder.Route(request)
                ?? (request.RequestUri!.AbsolutePath == FetchRoute
                    ? Ok("""{"success":true,"written":{"flowRegime":"steady"}}""") : null)
                ?? RouteSubscription(request, ids,
                    DeclaredModel(new Edge("WillowBend", "flowsAs", "flashy", stale)),
                    siteValues: null, DeclaredArchetypes())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        recorder.Written.Should().BeEmpty();
    }

    // The words came from the fetches, but the declaration read can still fail — and the site has
    // already left the state that dispatches runs, so nothing retries by itself. The run must not
    // guess: no edge is written, and the failure is the run's to report.
    [Fact]
    public async Task A_failed_declaration_read_writes_no_edge()
    {
        var ids = CatchmentWithOneSource();
        var recorder = new EdgeRecorder();
        var subscriptions = 0;

        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
        {
            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/subscriptions"
                && ++subscriptions > 1)
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
            return recorder.Route(request)
                ?? (request.RequestUri!.AbsolutePath == FetchRoute
                    ? Ok("""{"success":true,"written":{"flowRegime":"steady"}}""") : null)
                ?? RouteSubscription(request, ids, DeclaredModel(), siteValues: null, DeclaredArchetypes())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        recorder.Written.Should().BeEmpty();
    }
}
