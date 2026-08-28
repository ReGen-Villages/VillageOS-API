using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

// Task #6789, driven end to end: a run asks only the calls whose answer is not already in, and records
// what each one came to. The ledger's own decisions are pinned in Services/CoverageLedgerTests; this
// covers what the endpoint composes from them, which is the whole point of the change — a site used to
// be discovered exactly once ever, and every source after the first was never called again.
public class HandleEndpointCoverageTests
{
    private const string FetchRoute = "/api/endpoints/tributary";

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Two sources cover the site. One already has a coverage carrying the instant its answer landed.
    private static Dictionary<string, Guid> Ids() =>
        new[]
        {
            "WillowBend", "Portugal", "Earth", "isIn", "covers", "resolvedBy", "is",
            "NationalFloodPortal", "NationalFloodPortalEndpoint", "OpenMeteo", "OpenMeteoEndpoint",
            "SourceCoverage", CoveringSourceResolver.AppliesToPredicate,
            CoveringSourceResolver.SourcedFromPredicate, "WillowBend × OpenMeteo",
        }.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static Edge[] Edges() =>
    [
        new Edge("WillowBend", "isIn", "Portugal"),
        new Edge("Portugal", "isIn", "Earth"),
        new Edge("NationalFloodPortal", "covers", "Portugal"),
        new Edge("NationalFloodPortal", "resolvedBy", "NationalFloodPortalEndpoint"),
        new Edge("OpenMeteo", "covers", "Earth"),
        new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
        new Edge("WillowBend × OpenMeteo", CoveringSourceResolver.AppliesToPredicate, "WillowBend"),
        new Edge("WillowBend × OpenMeteo", CoveringSourceResolver.SourcedFromPredicate, "OpenMeteo"),
    ];

    private static Archetype[] Archetypes() =>
        [new Archetype("SourceCoverage", CoveringSourceResolver.SourceCoverageArchetypeFlag)];

    // The whole of what the coverage Things buy: the source whose answer stands is not called, and the
    // one still outstanding is. Before this, the first source to answer ended discovery for both.
    [Fact]
    public async Task Handle_ASourceWhoseAnswerAlreadyStands_IsNotCalledAgain()
    {
        var ids = Ids();
        var fetched = new ConcurrentBag<string>();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute)
            {
                fetched.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return Ok("""{"success":true}""");
            }
            return RouteSubscription(
                       req, ids, Edges(), siteValues: null, Archetypes(),
                       [new Carrying("WillowBend × OpenMeteo", "resolvedAt", "\"2026-08-27T09:00:00Z\"")])
                   ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        fetched.Should().ContainSingle("the source that already answered is not asked again");
        fetched.Single().Should().Contain("NationalFloodPortalEndpoint");
        fetched.Single().Should().NotContain("OpenMeteoEndpoint");
    }

    // A model that never read the template declaring the coverage vocabulary still has sources worth
    // fetching. Recording nothing is right; discovering nothing would be worse than what this replaces.
    [Fact]
    public async Task Handle_AModelWithNoCoverageVocabulary_StillCallsEverySource()
    {
        var ids = Ids();
        var fetched = new ConcurrentBag<string>();
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute)
            {
                fetched.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return Ok("""{"success":true}""");
            }
            return RouteSubscription(req, ids, Edges(), siteValues: null, archetypes: null)
                   ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        fetched.Should().HaveCount(2);
    }
}
