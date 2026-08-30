using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

// The division a run works out for itself, driven end to end (6851). The choosing is pinned in
// Helpers/HazardDivisionTests and the run's own decisions in Services/DivisionResolverTests; this covers
// what the endpoint composes from them, which is the half nothing else reaches: the two lookups are read
// out of the same model read the sources come from, and what they settle on has to address the gradings
// made on this run rather than the run after.
//
// Before this, a submitted site reached only the root Place, which carries no division code, so every
// grading was refused for an unfilled placeholder and the hazards table read as "no hazards here".
public class HandleEndpointDivisionTests
{
    private const string FetchRoute = "/api/endpoints/tributary";
    private const string AreaNameEndpoint = "area-name-at-position";
    private const string SearchEndpoint = "hazard-division-search";

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Dictionary<string, Guid> Ids() =>
        new[]
        {
            "WillowBend", "Earth", "isIn", "covers", "resolvedBy",
            "ThinkHazard", "hazard-grading", AreaNameEndpoint, SearchEndpoint,
        }.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static Edge[] Edges() =>
    [
        new Edge("WillowBend", "isIn", "Earth"),
        new Edge("ThinkHazard", "covers", "Earth"),
        new Edge("ThinkHazard", "resolvedBy", "hazard-grading"),
    ];

    // The two lookups as a template declares them: a mark each, an address, and — on the search — the
    // properties a run writes what it settled on into.
    private static Carrying[] TheTwoLookups() =>
    [
        new Carrying(AreaNameEndpoint, CoveringSourceResolver.AreaNameLookupFlag, "true"),
        new Carrying(AreaNameEndpoint, "url", "\"https://example.test/reverse?lat={latitude}&lon={longitude}\""),
        new Carrying(SearchEndpoint, CoveringSourceResolver.HazardDivisionLookupFlag, "true"),
        new Carrying(SearchEndpoint, "url", "\"https://example.test/administrativedivision?q={areaName}\""),
        new Carrying(SearchEndpoint, "divisionCodeProperty", "\"hazardPortalDivision\""),
        new Carrying(SearchEndpoint, "divisionNameProperty", "\"hazardPortalDivisionName\""),
    ];

    private static Dictionary<string, string> Coordinates() =>
        new() { ["latitude"] = "39.5012", ["longitude"] = "-8.4137" };

    private const string InPortugal = """
        {"address": {"county": "Santarem", "country": "Portugal"}}
        """;

    private const string DivisionsNamedSantarem = """
        {"data": [
          {"code": 2409, "admin0": "Portugal", "admin1": "Santarem"},
          {"code": 8836, "admin0": "Brazil", "admin1": "Para", "admin2": "Santarem"},
          {"code": 24889, "admin0": "Portugal", "admin1": "Santarem", "admin2": "Santarem"}]}
        """;

    [Fact]
    public async Task Handle_ASiteWhoseModelCarriesNoDivision_GradesAtTheOneItWorksOut()
    {
        var ids = Ids();
        var graded = new ConcurrentBag<string>();
        var written = new ConcurrentBag<string>();

        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == FetchRoute)
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                if (body.Contains(AreaNameEndpoint, StringComparison.Ordinal)) return Ok(InPortugal);
                if (body.Contains(SearchEndpoint, StringComparison.Ordinal)) return Ok(DivisionsNamedSantarem);
                graded.Add(body);
                return Ok("""{"success":true}""");
            }

            if (path.Contains("/properties/", StringComparison.Ordinal) && path.EndsWith("/facts", StringComparison.Ordinal))
            {
                written.Add(path);
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"sequenceNumber":1}""", Encoding.UTF8, "application/json"),
                };
            }

            return RouteSubscription(req, ids, Edges(), Coordinates(), archetypes: null, carrying: TheTwoLookups())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        written.Should().Contain(path => path.Contains("hazardPortalDivision/facts", StringComparison.Ordinal));
        written.Should().Contain(path => path.Contains("hazardPortalDivisionName/facts", StringComparison.Ordinal));

        // The finer of the two Portuguese divisions, and this run's own grading addressed with it.
        graded.Should().ContainSingle().Which.Should().Contain("24889");
    }

    // A run that made both lookups on every dispatch would call two providers for a site that needs
    // neither, and would resolve over a division a project put on its own Place.
    [Fact]
    public async Task Handle_ASiteWhosePlaceCarriesADivision_LooksNothingUp()
    {
        var ids = Ids();
        var asked = new ConcurrentBag<string>();
        var places = new Dictionary<string, string>(Coordinates()) { ["hazardPortalDivision"] = "2409" };

        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = req =>
        {
            if (req.RequestUri!.AbsolutePath == FetchRoute)
            {
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                asked.Add(body);
                return Ok("""{"success":true}""");
            }

            return RouteSubscription(req, ids, Edges(), places, archetypes: null, carrying: TheTwoLookups())
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/handle", new { subjectId = ids["WillowBend"] });
        await factory.RunsStarted();

        asked.Should().ContainSingle().Which.Should().Contain("2409");
    }
}
