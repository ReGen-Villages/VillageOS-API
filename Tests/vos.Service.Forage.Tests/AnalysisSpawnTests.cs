using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

// What happens once the run finishes: the site's analysis starts, whatever mixture of sources resolved.
// Driven through /handle, because the thing worth pinning is that discovery and the analysis are one
// path a caller triggers rather than two a caller has to sequence.
//
// The analysis is started by relating the site's STUDY to each compute service, never by calling one.
// A connection bound to a service is a handled predicate, so the edge is the trigger — and every test
// here asserts on the relationships written rather than on any call to a service, because a second way
// to start an analysis is what these are meant to keep from appearing.
//
// The subject is the study rather than the site because a compute service reads its inputs off the
// study. An edge naming the site would dispatch against a Thing carrying none of them.
public class AnalysisSpawnTests
{
    private const string FetchRoute = "/api/endpoints/tributary";

    private static Dictionary<string, Guid> Names(params string[] names) =>
        names.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // The archetype the connections are found by, and the prototypes the analysis edges point at.
    private static Archetype[] AnalysisArchetypes() =>
    [
        new("SiteAnalysisConnection", CoveringSourceResolver.SiteAnalysisConnectionFlag),
        new("EnergyBalance prototype"),
        new("WaterReserve prototype"),
    ];

    private static Dictionary<string, Guid> SiteWithOneSource() => Names(
        "WillowBend", "Portugal", "isIn", "covers", "resolvedBy", "studies", "has", "is",
        "OpenMeteo", "OpenMeteoEndpoint", "WillowBendStudy",
        "SiteAnalysisConnection", "balancesEnergy", "balancesEnergy service", "EnergyBalance prototype");

    // One source, a study, and one connection the model marks as one a site analysis starts.
    private static Edge[] OneSourceAndOneBalance() =>
    [
        new Edge("WillowBend", "isIn", "Portugal"),
        new Edge("OpenMeteo", "covers", "Portugal"),
        new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
        .. StudyAndBalance(),
    ];

    private static Edge[] StudyAndBalance() =>
    [
        new Edge("WillowBendStudy", "studies", "WillowBend"),
        new Edge("balancesEnergy", "is", "SiteAnalysisConnection"),
        new Edge("balancesEnergy", "has", "balancesEnergy service"),
        new Edge("balancesEnergy service", "is", "EnergyBalance prototype"),
    ];

    // Records the relationships the run writes.
    private sealed class SpawnRecorder
    {
        public List<(Guid Subject, Guid Predicate, Guid Target)> Written { get; } = new();

        public HttpResponseMessage? Route(HttpRequestMessage request)
        {
            if (request.Method != HttpMethod.Post || request.RequestUri!.AbsolutePath != "/api/relationships")
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
        Dictionary<string, Guid> ids, Edge[] edges, SpawnRecorder spawns,
        Func<HttpRequestMessage, HttpResponseMessage?>? intercept = null)
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
            intercept?.Invoke(request)
            ?? spawns.Route(request)
            ?? (request.RequestUri!.AbsolutePath == FetchRoute ? Ok("""{"success":true}""") : null)
            ?? RouteSubscription(request, ids, edges, siteValues: null, AnalysisArchetypes())
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        return await client.PostAsJsonAsync("/handle", new { siteId = ids["WillowBend"] });
    }

    [Fact]
    public async Task Handle_EverySourceResolved_RelatesTheStudyToItsComputeService()
    {
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder();

        var response = await RunDiscovery(ids, OneSourceAndOneBalance(), spawns);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"started\":true");
        spawns.Written.Should().ContainSingle()
            .Which.Should().Be((
                ids["WillowBendStudy"], ids["balancesEnergy"], ids["EnergyBalance prototype"]));
    }

    [Fact]
    public async Task Handle_ModelMarksASecondConnection_StartsBothWithoutNamingEither()
    {
        // The point of finding connections by mark: a balance is added to the model, not to this code.
        var ids = SiteWithOneSource();
        foreach (var name in new[] { "reservesWater", "reservesWater service", "WaterReserve prototype" })
            ids[name] = Guid.NewGuid();
        var spawns = new SpawnRecorder();
        Edge[] edges =
        [
            .. OneSourceAndOneBalance(),
            new Edge("reservesWater", "is", "SiteAnalysisConnection"),
            new Edge("reservesWater", "has", "reservesWater service"),
            new Edge("reservesWater service", "is", "WaterReserve prototype"),
        ];

        var response = await RunDiscovery(ids, edges, spawns);

        (await response.Content.ReadAsStringAsync()).Should().Contain("\"started\":true");
        spawns.Written.Should().BeEquivalentTo(new[]
        {
            (ids["WillowBendStudy"], ids["balancesEnergy"], ids["EnergyBalance prototype"]),
            (ids["WillowBendStudy"], ids["reservesWater"], ids["WaterReserve prototype"]),
        });
    }

    [Fact]
    public async Task Handle_ConnectionIsNotMarked_IsNotStarted()
    {
        // An unmarked connection is an ordinary one — a service bound for some other purpose — and
        // starting it would dispatch a service against a study it knows nothing about.
        var ids = SiteWithOneSource();
        ids["ordinary"] = Guid.NewGuid();
        ids["ordinary service"] = Guid.NewGuid();
        var spawns = new SpawnRecorder();
        Edge[] edges =
        [
            .. OneSourceAndOneBalance(),
            new Edge("ordinary", "has", "ordinary service"),
            new Edge("ordinary service", "is", "EnergyBalance prototype"),
        ];

        await RunDiscovery(ids, edges, spawns);

        spawns.Written.Should().ContainSingle()
            .Which.Predicate.Should().Be(ids["balancesEnergy"]);
    }

    [Fact]
    public async Task Handle_EverySourceFailed_StartsTheAnalysisAnyway()
    {
        // The case the analysis most needs to run for: a planner has to be told the balances could
        // not be computed and why, which cannot happen if nothing ever runs.
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder();

        var response = await RunDiscovery(ids, OneSourceAndOneBalance(), spawns,
            request => request.RequestUri!.AbsolutePath == FetchRoute
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                    { Content = new StringContent("portal down", Encoding.UTF8, "text/plain") }
                : null);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo").And.Contain("\"resolved\":[]");
        body.Should().Contain("\"started\":true");
        spawns.Written.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_NoSourceCoversTheSite_StartsTheAnalysisAnyway()
    {
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder();
        Edge[] edges = [new Edge("WillowBend", "isIn", "Portugal"), .. StudyAndBalance()];

        var response = await RunDiscovery(ids, edges, spawns);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        spawns.Written.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_SiteHasNoStudy_StartsNothingAndSaysWhy()
    {
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder();
        Edge[] edges =
        [
            new Edge("WillowBend", "isIn", "Portugal"),
            new Edge("OpenMeteo", "covers", "Portugal"),
            new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
            new Edge("balancesEnergy", "is", "SiteAnalysisConnection"),
            new Edge("balancesEnergy", "has", "balancesEnergy service"),
            new Edge("balancesEnergy service", "is", "EnergyBalance prototype"),
        ];

        var response = await RunDiscovery(ids, edges, spawns);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo");
        body.Should().Contain("\"started\":false").And.Contain("no study to analyse");
        spawns.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ModelMarksNoAnalysisConnection_StartsNothingAndSaysWhy()
    {
        // The state a model seeded without its compute connections is in. It is reported as a gap in
        // the model rather than as a discovery that failed, because discovery did not fail.
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder();
        Edge[] edges =
        [
            new Edge("WillowBend", "isIn", "Portugal"),
            new Edge("OpenMeteo", "covers", "Portugal"),
            new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
            new Edge("WillowBendStudy", "studies", "WillowBend"),
        ];

        var response = await RunDiscovery(ids, edges, spawns);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain("\"started\":false").And.Contain("marks no connection");
        spawns.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_StartingTheAnalysisFails_StillReportsWhatDiscoveryFound()
    {
        // Discovery's observations are already written by the time the analysis is asked for. Losing
        // the report because a service could not be started would throw away work that succeeded.
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder();

        var response = await RunDiscovery(ids, OneSourceAndOneBalance(), spawns,
            request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/relationships"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo").And.Contain("\"unresolved\":[]");
        body.Should().Contain("\"started\":false").And.Contain("balancesEnergy");
    }

    [Fact]
    public async Task Handle_RelationshipWriteCannotBeReached_ReportsItRatherThanFailingTheRun()
    {
        // The write is the last thing a run does, and it reaches the network. An exception raised
        // there must not escape as a 500 that throws away observations already written.
        var ids = SiteWithOneSource();
        var spawns = new SpawnRecorder();

        var response = await RunDiscovery(ids, OneSourceAndOneBalance(), spawns,
            request => request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/relationships"
                ? throw new HttpRequestException("the model is unreachable")
                : null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("OpenMeteo").And.Contain("\"started\":false");
    }
}
