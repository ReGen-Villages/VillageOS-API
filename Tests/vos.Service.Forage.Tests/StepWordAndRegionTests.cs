using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

public class StepWordAndRegionTests
{
    private const string FetchRoute = "/api/endpoints/tributary";

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task<JsonElement> StepAsync(Func<HttpRequestMessage, HttpResponseMessage> route, string step, object inputs)
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = route;
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/handle", new
        {
            runId = Guid.NewGuid(),
            nodeId = Guid.NewGuid(),
            @params = new { step },
            inputs,
        });
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Dictionary<string, Guid> CatchmentIds() =>
        new[] { "WillowBend", "FlowSurvey", "is", "Catchment", "FlowRegime", "flowsAs", "steady", "flashy", "DiscoveredVocabulary" }
            .ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static Archetype[] DeclaredArchetypes() =>
    [
        new("Catchment"),
        new("FlowRegime", Values: new Dictionary<string, string> { [DiscoveredVocabularyResolver.ResolvedFromProperty] = "\"flowRegime\"" }),
        new("DiscoveredVocabulary", BoundBy: DiscoveredVocabularyResolver.DiscoveredVocabularyRole),
    ];

    private static Edge[] DeclaredModel() =>
    [
        new Edge("WillowBend", "is", "Catchment"),
        new Edge("Catchment", "flowsAs", "FlowRegime"),
        new Edge("FlowRegime", "is", "DiscoveredVocabulary"),
        new Edge("steady", "is", "FlowRegime"),
        new Edge("flashy", "is", "FlowRegime"),
    ];

    private static object Resolved(Dictionary<string, Guid> ids, string word) => new
    {
        subjectId = ids["WillowBend"],
        sourceId = ids["FlowSurvey"],
        source = "FlowSurvey",
        subject = "WillowBend",
        written = new { flowRegime = word },
    };

    private static Func<HttpRequestMessage, HttpResponseMessage> Catchment(Dictionary<string, Guid> ids, HttpStatusCode? relatingAnswered = null) => request =>
        RelationshipWrite.In(request) is not null
            ? relatingAnswered is { } status
                ? new HttpResponseMessage(status) { Content = new StringContent("""{"error":"the platform's words"}""", Encoding.UTF8, "application/json") }
                : Ok("{}")
            : RouteSnapshotRead(request, ids, DeclaredModel(), siteValues: null, DeclaredArchetypes()) ?? new HttpResponseMessage(HttpStatusCode.NotFound);

    [Fact]
    public async Task Resolve_words_answers_the_relationships_written_and_the_words_the_model_does_not_hold()
    {
        var ids = CatchmentIds();

        var answer = await StepAsync(Catchment(ids), "resolveWords", new { resolved = new[] { Resolved(ids, "steady") } });

        answer.GetProperty("result").GetString().Should().Be("success");
        var written = answer.GetProperty("outputs").GetProperty("written").EnumerateArray().Should().ContainSingle().Subject;
        (written.GetProperty("subjectId").GetGuid(), written.GetProperty("word").GetString(), written.GetProperty("memberId").GetGuid())
            .Should().Be((ids["WillowBend"], "steady", ids["steady"]));

        var unknown = await StepAsync(Catchment(ids), "resolveWords", new { resolved = new[] { Resolved(ids, "torrential") } });
        unknown.GetProperty("outputs").GetProperty("unknownWords").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("word").GetString().Should().Be("torrential");
    }

    [Fact]
    public async Task Resolve_words_whose_relationship_the_platform_did_not_answer_answers_a_failure_that_may_be_tried_again()
    {
        var ids = CatchmentIds();

        var answer = await StepAsync(Catchment(ids, HttpStatusCode.ServiceUnavailable), "resolveWords", new { resolved = new[] { Resolved(ids, "steady") } });

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.TryGetProperty("retry", out _).Should().BeTrue();
        answer.GetProperty("error").GetString().Should().Be("Could not write the relationships for: 'steady' on WillowBend.");
    }

    [Fact]
    public async Task Resolve_words_whose_relationship_the_platform_refuses_for_good_reports_it_and_succeeds()
    {
        var ids = CatchmentIds();

        var answer = await StepAsync(Catchment(ids, HttpStatusCode.Forbidden), "resolveWords", new { resolved = new[] { Resolved(ids, "steady") } });

        answer.GetProperty("result").GetString().Should().Be("success");
        var refused = answer.GetProperty("outputs").GetProperty("refusedWords").EnumerateArray().Should().ContainSingle().Subject;
        refused.GetProperty("word").GetString().Should().Be("steady");
        refused.GetProperty("subject").GetString().Should().Be("WillowBend");
        refused.GetProperty("reason").GetString().Should().Be("the platform's words");
    }

    [Fact]
    public async Task Resolve_words_whose_relationship_is_rejected_while_it_still_resolves_fails_at_once_with_the_platforms_words()
    {
        var ids = CatchmentIds();

        var answer = await StepAsync(Catchment(ids, HttpStatusCode.BadRequest), "resolveWords", new { resolved = new[] { Resolved(ids, "steady") } });

        answer.GetProperty("result").GetString().Should().Be("fail");
        answer.TryGetProperty("retry", out _).Should().BeFalse();
        answer.GetProperty("error").GetString().Should().Contain("'steady' on WillowBend").And.Contain("the platform's words");
    }

    private const string AreaNameEndpoint = "area-name-at-position";
    private const string SearchEndpoint = "hazard-division-search";

    [Fact]
    public async Task Resolve_region_adds_the_division_it_works_out_to_the_sites_address_values()
    {
        var ids = new[] { "WillowBend", "Earth", "isIn", "covers", "resolvedBy", "ThinkHazard", "hazard-grading", AreaNameEndpoint, SearchEndpoint }
            .ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);
        Edge[] edges = [new Edge("WillowBend", "isIn", "Earth"), new Edge("ThinkHazard", "covers", "Earth"), new Edge("ThinkHazard", "resolvedBy", "hazard-grading")];
        Binding[] lookups = [new Binding(CoveringSourceResolver.AreaNameLookupRole, AreaNameEndpoint), new Binding(CoveringSourceResolver.HazardDivisionLookupRole, SearchEndpoint)];
        Carrying[] lookupSettings =
        [
            new Carrying(AreaNameEndpoint, "url", "\"https://example.test/reverse?lat={latitude}&lon={longitude}\""),
            new Carrying(SearchEndpoint, "url", "\"https://example.test/administrativedivision?q={areaName}\""),
            new Carrying(SearchEndpoint, "divisionCodeProperty", "\"hazardPortalDivision\""),
            new Carrying(SearchEndpoint, "divisionNameProperty", "\"hazardPortalDivisionName\""),
        ];
        var coordinates = new Dictionary<string, string> { ["latitude"] = "39.5012", ["longitude"] = "-8.4137" };

        var answer = await StepAsync(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == FetchRoute)
            {
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return body.Contains(AreaNameEndpoint, StringComparison.Ordinal)
                    ? Ok("""{"address": {"county": "Santarem", "country": "Portugal"}}""")
                    : Ok("""{"data": [{"code": 24889, "admin0": "Portugal", "admin1": "Santarem", "admin2": "Santarem"}]}""");
            }
            if (path.EndsWith("/facts", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"sequenceNumber":1}""", Encoding.UTF8, "application/json") };
            return RouteSnapshotRead(request, ids, edges, coordinates, archetypes: null, carrying: lookupSettings, bindings: lookups)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }, "resolveRegion", new { site = ids["WillowBend"].ToString() });

        var address = answer.GetProperty("outputs").GetProperty("address");
        address.GetProperty("hazardPortalDivision").GetString().Should().Be("24889");
        address.GetProperty("latitude").GetString().Should().Be("39.5012");
    }
}
