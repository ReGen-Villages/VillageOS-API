using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

// Task #6812, driven end to end: a dispatch naming a source offers it to every site under the Places it
// covers — a coverage minted per call those sites would make, nothing fetched, and the source stamped as
// worked out. The walk itself is pinned in Helpers/SourceReachTests and the choice of run in
// Helpers/SubjectKindTests; this covers what the endpoint composes from them.
//
// What it buys: a source added to the catalogue after a site was discovered reaches that site. One
// outstanding coverage puts the site back in the state a run is dispatched by, and that run asks only
// this source.
public class HandleEndpointSourceOfferedTests
{
    private const string FetchRoute = "/api/endpoints/tributary";
    private const string MintRoute = "/api/things";
    private const string RelateRoute = "/api/relationships";
    private const string SubscribeRoute = "/api/subscriptions";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Two sites in two countries, two sources: one covers only Portugal, the other the whole root.
    private static Dictionary<string, Guid> Ids() =>
        new[]
        {
            "WillowBend", "Oakhollow", "Portugal", "Spain", "Earth", "Site", "DataSource",
            "isIn", "covers", "resolvedBy", "is",
            "NationalFloodPortal", "NationalFloodPortalEndpoint", "OpenMeteo", "OpenMeteoEndpoint",
            "SourceCoverage", CoveringSourceResolver.AppliesToPredicate, CoveringSourceResolver.SourcedFromPredicate,
            "WillowBend × NationalFloodPortal",
        }.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static Edge[] Edges() =>
    [
        new Edge("WillowBend", "is", "Site"),
        new Edge("Oakhollow", "is", "Site"),
        new Edge("WillowBend", "isIn", "Portugal"),
        new Edge("Oakhollow", "isIn", "Spain"),
        new Edge("Portugal", "isIn", "Earth"),
        new Edge("Spain", "isIn", "Earth"),
        new Edge("NationalFloodPortal", "is", "DataSource"),
        new Edge("NationalFloodPortal", "covers", "Portugal"),
        new Edge("NationalFloodPortal", "resolvedBy", "NationalFloodPortalEndpoint"),
        new Edge("OpenMeteo", "is", "DataSource"),
        new Edge("OpenMeteo", "covers", "Earth"),
        new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
    ];

    private static Edge[] AlreadyOffered() =>
    [
        .. Edges(),
        new Edge("WillowBend × NationalFloodPortal", CoveringSourceResolver.AppliesToPredicate, "WillowBend"),
        new Edge("WillowBend × NationalFloodPortal", CoveringSourceResolver.SourcedFromPredicate, "NationalFloodPortal"),
    ];

    private static Archetype[] Archetypes() =>
    [
        new Archetype("Site", CoveringSourceResolver.SiteArchetypeFlag),
        new Archetype("SourceCoverage", CoveringSourceResolver.SourceCoverageArchetypeFlag),
        new Archetype("DataSource"),
    ];

    // Everything a run writes, by route, so a test reads as what the run did to the model.
    private sealed class Writes
    {
        public ConcurrentBag<string> Fetched { get; } = new();
        public ConcurrentBag<string> Minted { get; } = new();
        public ConcurrentBag<string> Related { get; } = new();
        public ConcurrentBag<string> Facts { get; } = new();

        public HttpResponseMessage? Route(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            if (path == FetchRoute)
            {
                Fetched.Add(body);
                return Json(HttpStatusCode.OK, """{"success":true}""");
            }
            if (path == MintRoute && request.Method == HttpMethod.Post)
            {
                Minted.Add(JsonDocument.Parse(body).RootElement.EnumerateObject()
                    .Single(property => property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                    .Value.GetString()!);
                return Json(HttpStatusCode.Created, "{\"id\":\"" + Guid.NewGuid() + "\"}");
            }
            if (path == RelateRoute && request.Method == HttpMethod.Post)
            {
                Related.Add(body);
                return Json(HttpStatusCode.Created, "{}");
            }
            if (path.EndsWith("/facts", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                Facts.Add(path);
                return Json(HttpStatusCode.Created, """{"sequenceNumber":1}""");
            }
            return null;
        }
    }

    private static string StampOn(IReadOnlyDictionary<string, Guid> ids, string thing) =>
        MyceliumRoutes.ThingPropertyFacts(ids[thing], CoverageLedger.CoverageWorkedOutAtProperty);

    private static async Task<Writes> Dispatch(
        IReadOnlyDictionary<string, Guid> ids, string subject, Edge[] edges,
        Func<HttpRequestMessage, HttpResponseMessage?>? before = null)
    {
        var writes = new Writes();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = request =>
            before?.Invoke(request)
            ?? writes.Route(request)
            ?? RouteSubscription(request, ids, edges, siteValues: null, Archetypes())
            ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids[subject] });
        await factory.RunsStarted();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return writes;
    }

    [Fact]
    public async Task Handle_ADispatchNamingASource_MintsACoverageForEverySiteUnderItsPlaces()
    {
        var ids = Ids();

        var writes = await Dispatch(ids, "NationalFloodPortal", Edges());

        writes.Minted.Should().ContainSingle().Which.Should().Be("WillowBend × NationalFloodPortal",
            "Oakhollow is in Spain, which this source does not cover, however many others do");
        var related = string.Join("\n", writes.Related);
        related.Should().Contain(ids["SourceCoverage"].ToString(), "the coverage is the archetype's");
        related.Should().Contain(ids["WillowBend"].ToString(), "it applies to the site");
        related.Should().Contain(ids["NationalFloodPortal"].ToString(), "it is sourced from the source");
    }

    // Nothing is fetched here. A minted coverage is outstanding, and that is what puts the site back in
    // the state a run is dispatched by — the run that fetches is the site's own, and it asks only this
    // source.
    [Fact]
    public async Task Handle_ADispatchNamingASource_FetchesNothingAndStampsTheSource()
    {
        var ids = Ids();

        var writes = await Dispatch(ids, "NationalFloodPortal", Edges());

        writes.Fetched.Should().BeEmpty();
        writes.Facts.Should().ContainSingle().Which.Should().Be(StampOn(ids, "NationalFloodPortal"));
    }

    // Offered once already: the coverage stands, whether or not the source has answered, and a second
    // one would let the site be judged against one and recorded on the other.
    [Fact]
    public async Task Handle_ASourceAlreadyOffered_MintsNothingASecondTime()
    {
        var ids = Ids();

        var writes = await Dispatch(ids, "NationalFloodPortal", AlreadyOffered());

        writes.Minted.Should().BeEmpty();
        writes.Related.Should().BeEmpty();
        writes.Facts.Should().ContainSingle().Which.Should().Be(StampOn(ids, "NationalFloodPortal"));
    }

    // A source stamped after a read that failed would be recorded as offered to every site it covers
    // while having reached none, and nothing would ever offer it again. Written nothing, it stays in the
    // state that dispatched it and is driven again.
    [Fact]
    public async Task Handle_ASourceReadThatFails_WritesNothingAndStampsNothing()
    {
        var ids = Ids();
        var reads = 0;

        var writes = await Dispatch(ids, "NationalFloodPortal", Edges(), request =>
            request.Method == HttpMethod.Post
            && request.RequestUri!.AbsolutePath == SubscribeRoute
            && Interlocked.Increment(ref reads) == 2
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : null);

        reads.Should().Be(2, "the subject's kind is read, then the sites the source reaches");
        writes.Minted.Should().BeEmpty();
        writes.Facts.Should().BeEmpty();
    }

    // The site's run would stamp a Place as worked out and a source's run would offer it to nothing and
    // do the same. A subject that is neither is a dispatch the model got wrong, and the run says so by
    // writing nothing.
    [Fact]
    public async Task Handle_ASubjectThatIsNeitherSiteNorSource_WritesNothing()
    {
        var ids = Ids();

        var writes = await Dispatch(ids, "Portugal", Edges());

        writes.Fetched.Should().BeEmpty();
        writes.Minted.Should().BeEmpty();
        writes.Facts.Should().BeEmpty();
    }

    // With two kinds of subject in the model, a site is still fetched: the mark decides, and this is
    // the branch every existing site run now goes down.
    [Fact]
    public async Task Handle_ADispatchNamingASite_StillFetchesEverySourceCoveringIt()
    {
        var ids = Ids();

        var writes = await Dispatch(ids, "WillowBend", Edges());

        writes.Fetched.Should().HaveCount(2);
        writes.Facts.Should().Contain(StampOn(ids, "WillowBend"));
    }
}
