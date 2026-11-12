using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using Xunit;
using static vos.Service.Forage.Tests.ModelSnapshotStub;

namespace vos.Service.Forage.Tests;

public class StepRequestTests
{
    private const string FetchRoute = "/api/endpoints/tributary";

    // Two sources cover the site; OpenMeteo already holds a record for it. The site has a study and one analysis.
    private static Dictionary<string, Guid> Ids() =>
        new[]
        {
            "WillowBend", "Site", "Portugal", "Earth", "isIn", "covers", "resolvedBy", "is", "studies", "has",
            CoveringSourceResolver.ResolvesOntoPredicate, "Assessment",
            "NationalFloodPortal", "NationalFloodPortalEndpoint", "OpenMeteo", "OpenMeteoEndpoint",
            "SourceCoverage", CoveringSourceResolver.AppliesToPredicate, CoveringSourceResolver.SourcedFromPredicate,
            "WillowBend × OpenMeteo", "WillowBendStudy",
            "SiteAnalysisConnection", "balancesEnergy", "balancesEnergy service", "EnergyBalance prototype",
        }.ToDictionary(name => name, _ => Guid.NewGuid(), StringComparer.Ordinal);

    private static Edge[] Edges(bool withStudy = true) =>
    [
        new Edge("WillowBend", "is", "Site"),
        new Edge("WillowBend", "isIn", "Portugal"),
        new Edge("Portugal", "isIn", "Earth"),
        new Edge("NationalFloodPortal", "covers", "Portugal"),
        new Edge("NationalFloodPortal", "resolvedBy", "NationalFloodPortalEndpoint"),
        new Edge("OpenMeteo", "covers", "Earth"),
        new Edge("OpenMeteo", "resolvedBy", "OpenMeteoEndpoint"),
        new Edge("WillowBend × OpenMeteo", CoveringSourceResolver.AppliesToPredicate, "WillowBend"),
        new Edge("WillowBend × OpenMeteo", CoveringSourceResolver.SourcedFromPredicate, "OpenMeteo"),
        .. withStudy
            ? new[]
            {
                new Edge("WillowBendStudy", "studies", "WillowBend"),
                new Edge("balancesEnergy", "is", "SiteAnalysisConnection"),
                new Edge("balancesEnergy", "has", "balancesEnergy service"),
                new Edge("balancesEnergy service", "is", "EnergyBalance prototype"),
            }
            : [],
    ];

    private static Archetype[] Archetypes(bool withCoverage = true) =>
    [
        new Archetype("Site", BoundBy: CoveringSourceResolver.SiteRole),
        .. withCoverage ? new[] { new Archetype("SourceCoverage", BoundBy: CoveringSourceResolver.SourceCoverageRole) } : [],
        new Archetype("SiteAnalysisConnection", BoundBy: CoveringSourceResolver.SiteAnalysisConnectionRole),
        new Archetype("EnergyBalance prototype"),
    ];

    private static readonly IReadOnlyDictionary<string, string> SiteValues = new Dictionary<string, string> { ["lat"] = "39.4" };

    // Everything Forage writes to the model or asks of a source, by route.
    private sealed class Model
    {
        public Dictionary<string, Guid> Ids { get; init; } = StepRequestTests.Ids();
        public Edge[] Edges { get; init; } = StepRequestTests.Edges();
        public Archetype[] Archetypes { get; init; } = StepRequestTests.Archetypes();
        public Carrying[] Carrying { get; init; } = [];
        public bool ReadsFail { get; init; }
        public bool FactsFail { get; init; }
        public bool RelatingFails { get; init; }

        public ConcurrentBag<string> Fetched { get; } = new();
        public ConcurrentBag<(Guid Id, string Name)> Records { get; } = new();
        public ConcurrentBag<(Guid Thing, string Property)> Facts { get; } = new();
        public ConcurrentBag<(Guid Subject, Guid Predicate, Guid Target)> Related { get; } = new();

        public HttpResponseMessage Route(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
            if (path == FetchRoute)
            {
                Fetched.Add(body);
                return Json("""{"success":true}""");
            }
            if (path == "/api/model/fragment")
            {
                var thing = JsonDocument.Parse(body).RootElement.GetProperty("things")[0];
                Records.Add((thing.GetProperty("id").GetGuid(), thing.GetProperty("name").GetString()!));
                return Json("{}");
            }
            if (path.EndsWith("/facts", StringComparison.Ordinal) && FactsFail)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            if (path == "/api/relationships" && RelatingFails)
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("refused") };
            if (path.EndsWith("/facts", StringComparison.Ordinal))
            {
                var parts = path.Split('/');
                Facts.Add((Guid.Parse(parts[3]), parts[5]));
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"sequenceNumber":1}""", Encoding.UTF8, "application/json") };
            }
            if (path == "/api/relationships" && request.Method == HttpMethod.Post)
            {
                var written = JsonDocument.Parse(body).RootElement;
                Related.Add((written.GetProperty("subjectId").GetGuid(), written.GetProperty("predicateId").GetGuid(), written.GetProperty("targetId").GetGuid()));
                return Json("{}");
            }
            if (ReadsFail && path == "/api/subscriptions")
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return RouteSubscription(request, Ids, Edges, SiteValues.ToDictionary(value => value.Key, value => $"\"{value.Value}\""), Archetypes, Carrying)
                ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public Guid this[string name] => Ids[name];
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static object Site(Model model) => new { id = model["WillowBend"], name = "WillowBend" };

    private static async Task<JsonElement> StepAsync(Model model, string? step, object inputs)
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        factory.HandlerCallback = model.Route;
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/handle", new
        {
            runId = Guid.NewGuid(),
            nodeId = Guid.NewGuid(),
            @params = step is null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["step"] = step },
            inputs,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a step's failure is said in its answer, never in its status");
        factory.StartedSubjects.Should().BeEmpty("a step answers within its request and starts no run of its own");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string ResultOf(JsonElement answer) => answer.GetProperty("result").GetString()!;
    private static JsonElement Output(JsonElement answer, string port) => answer.GetProperty("outputs").GetProperty(port);
    private static bool MayBeTriedAgain(JsonElement answer) => answer.TryGetProperty("retry", out _);

    [Fact]
    public async Task Resolve_region_answers_the_sites_address_values()
    {
        var model = new Model();

        var answer = await StepAsync(model, "resolveRegion", new { site = Site(model) });

        ResultOf(answer).Should().Be("success");
        Output(answer, "address").GetProperty("lat").GetString().Should().Be("39.4");
    }

    [Theory]
    [InlineData("resolveRegion")]
    [InlineData("planCalls")]
    [InlineData("startAnalysis")]
    public async Task A_step_that_cannot_read_the_model_answers_a_failure_that_may_be_tried_again(string step)
    {
        var model = new Model { ReadsFail = true };

        var answer = await StepAsync(model, step, new { site = Site(model) });

        ResultOf(answer).Should().Be("fail");
        MayBeTriedAgain(answer).Should().BeTrue();
        answer.GetProperty("error").GetString().Should().Be($"Could not read which sources cover site {model["WillowBend"]}.");
        model.Records.Should().BeEmpty();
        model.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_calls_answers_only_the_calls_still_unanswered_and_writes_a_record_for_each_under_its_subject_and_source()
    {
        var model = new Model { Carrying = [new Carrying("WillowBend × OpenMeteo", "resolvedAt", "\"2026-08-27T09:00:00Z\"")] };

        var answer = await StepAsync(model, "planCalls", new { site = model["WillowBend"].ToString() });

        ResultOf(answer).Should().Be("success");
        var call = Output(answer, "calls").EnumerateArray().Should().ContainSingle("the source that already answered is not planned again").Subject;
        var expectedRecord = CoverageLedger.RecordIdentifierFor(model["WillowBend"], model["NationalFloodPortal"]);
        call.GetProperty("endpointName").GetString().Should().Be("NationalFloodPortalEndpoint");
        call.GetProperty("subjectId").GetGuid().Should().Be(model["WillowBend"]);
        call.GetProperty("sourceId").GetGuid().Should().Be(model["NationalFloodPortal"]);
        call.GetProperty("recordId").GetGuid().Should().Be(expectedRecord);
        call.GetProperty("addressParameters").GetProperty("lat").GetString().Should().Be("39.4");
        model.Records.Should().Equal((expectedRecord, "WillowBend × NationalFloodPortal"));
        model.Fetched.Should().BeEmpty("Plan calls fetches nothing; Tributary does");
    }

    [Fact]
    public async Task Plan_calls_addresses_each_call_with_the_values_resolve_region_answered()
    {
        var model = new Model();

        var answer = await StepAsync(model, "planCalls", new { site = Site(model), address = new { lat = "39.4", divisionCode = "24889" } });

        Output(answer, "calls").EnumerateArray()
            .Select(call => call.GetProperty("addressParameters").GetProperty("divisionCode").GetString())
            .Should().AllBe("24889").And.HaveCount(2);
    }

    private static object Planned(Model model, string source, Guid? recordId, long attempts = 0) => new
    {
        endpointName = source + "Endpoint",
        subjectId = model["WillowBend"],
        addressParameters = new { lat = "39.4" },
        sourceId = model[source],
        source,
        subject = "WillowBend",
        recordId,
        attempts,
    };

    [Fact]
    public async Task Record_results_records_what_each_call_came_to_and_answers_them_resolved_and_unresolved()
    {
        var model = new Model();
        var (flood, meteo) = (Guid.NewGuid(), model["WillowBend × OpenMeteo"]);

        var answer = await StepAsync(model, "recordResults", new
        {
            resolved = new[] { new { call = Planned(model, "NationalFloodPortal", flood), written = new { floodLevel = "low" } } },
            unresolved = new[] { new { call = Planned(model, "OpenMeteo", meteo, attempts: 2), reason = "503: maintenance" } },
        });

        ResultOf(answer).Should().Be("success");
        model.Facts.Where(fact => fact.Thing == flood).Select(fact => fact.Property)
            .Should().BeEquivalentTo("lastAttemptAt", "attempts", "resolvedAt");
        model.Facts.Where(fact => fact.Thing == meteo).Select(fact => fact.Property)
            .Should().BeEquivalentTo("lastAttemptAt", "attempts", "failureReason");
        var resolved = Output(answer, "resolved").EnumerateArray().Should().ContainSingle().Subject;
        resolved.GetProperty("source").GetString().Should().Be("NationalFloodPortal");
        resolved.GetProperty("written").GetProperty("floodLevel").GetString().Should().Be("low");
        var unresolved = Output(answer, "unresolved").EnumerateArray().Should().ContainSingle().Subject;
        unresolved.GetProperty("reason").GetString().Should().Be("503: maintenance");
    }

    [Fact]
    public async Task Record_results_handed_no_failures_records_the_calls_that_resolved()
    {
        var model = new Model();
        var flood = Guid.NewGuid();

        var answer = await StepAsync(model, "recordResults", new { resolved = new[] { new { call = Planned(model, "NationalFloodPortal", flood), written = (object?)null } } });

        ResultOf(answer).Should().Be("success");
        Output(answer, "unresolved").GetArrayLength().Should().Be(0);
        model.Facts.Should().Contain((flood, "resolvedAt"));
    }

    [Fact]
    public async Task Record_results_handed_an_outcome_with_no_call_fails_saying_so()
    {
        var model = new Model();

        var answer = await StepAsync(model, "recordResults", new { resolved = new[] { new { written = new { a = "b" } } } });

        ResultOf(answer).Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Be("An outcome reaching Record results carries no call it can match to its record.");
    }

    [Fact]
    public async Task Resolve_words_with_nothing_written_answers_no_relationships_and_no_unknown_words()
    {
        var model = new Model();

        var answer = await StepAsync(model, "resolveWords", new { resolved = Array.Empty<object>() });

        ResultOf(answer).Should().Be("success");
        Output(answer, "written").GetArrayLength().Should().Be(0);
        Output(answer, "unknownWords").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Resolve_words_that_cannot_read_the_declarations_answers_a_failure_that_may_be_tried_again()
    {
        var model = new Model { ReadsFail = true };
        var resolved = new
        {
            subjectId = model["WillowBend"], sourceId = model["NationalFloodPortal"], source = "NationalFloodPortal",
            subject = "WillowBend", written = new { floodLevel = "low" }, reason = (string?)null,
        };

        var answer = await StepAsync(model, "resolveWords", new { resolved = new[] { resolved } });

        ResultOf(answer).Should().Be("fail");
        MayBeTriedAgain(answer).Should().BeTrue();
    }

    [Fact]
    public async Task Start_analysis_stamps_the_site_and_starts_its_analysis()
    {
        var model = new Model();

        var answer = await StepAsync(model, "startAnalysis", new { site = Site(model) });

        ResultOf(answer).Should().Be("success");
        model.Facts.Should().Contain((model["WillowBend"], CoverageLedger.CoverageMatchedAtProperty));
        model.Related.Should().Contain((model["WillowBendStudy"], model["balancesEnergy"], model["EnergyBalance prototype"]));
        Output(answer, "started").EnumerateArray().Select(name => name.GetString()).Should().Equal("balancesEnergy");
    }

    [Fact]
    public async Task Start_analysis_for_a_site_with_no_study_starts_nothing_and_says_why()
    {
        var model = new Model { Edges = Edges(withStudy: false) };

        var answer = await StepAsync(model, "startAnalysis", new { site = Site(model) });

        ResultOf(answer).Should().Be("success");
        Output(answer, "started").GetArrayLength().Should().Be(0);
        Output(answer, "reason").GetString().Should().Be("The site has no study to analyse.");
        model.Facts.Should().Contain((model["WillowBend"], CoverageLedger.CoverageMatchedAtProperty), "a site no source or study covers was still looked at");
    }

    [Fact]
    public async Task Offer_a_source_records_the_calls_outstanding_for_the_sites_it_covers()
    {
        var model = new Model();

        var answer = await StepAsync(model, "offerSource", new { source = model["NationalFloodPortal"].ToString() });

        ResultOf(answer).Should().Be("success");
        var expectedRecord = CoverageLedger.RecordIdentifierFor(model["WillowBend"], model["NationalFloodPortal"]);
        var outstanding = Output(answer, "outstanding").EnumerateArray().Should().ContainSingle().Subject;
        outstanding.GetProperty("subjectId").GetGuid().Should().Be(model["WillowBend"]);
        outstanding.GetProperty("recordId").GetGuid().Should().Be(expectedRecord);
        model.Records.Select(record => record.Id).Should().Equal(expectedRecord);
        model.Facts.Should().Contain((model["NationalFloodPortal"], CoverageLedger.CoverageMatchedAtProperty));
        model.Fetched.Should().BeEmpty();
    }

    [Fact]
    public async Task Offer_a_source_in_a_model_with_no_coverage_archetype_is_a_final_failure()
    {
        var model = new Model { Archetypes = Archetypes(withCoverage: false) };

        var answer = await StepAsync(model, "offerSource", new { source = model["NationalFloodPortal"].ToString() });

        ResultOf(answer).Should().Be("fail");
        MayBeTriedAgain(answer).Should().BeFalse();
        model.Facts.Should().BeEmpty("a source offered to no site is not stamped as offered");
    }

    [Fact]
    public async Task Offer_a_source_that_cannot_be_read_answers_a_failure_that_may_be_tried_again()
    {
        var model = new Model { ReadsFail = true };

        var answer = await StepAsync(model, "offerSource", new { source = model["NationalFloodPortal"].ToString() });

        ResultOf(answer).Should().Be("fail");
        MayBeTriedAgain(answer).Should().BeTrue();
    }

    [Fact]
    public async Task Plan_calls_in_a_model_with_no_coverage_archetype_plans_every_call_and_records_none()
    {
        var model = new Model { Archetypes = Archetypes(withCoverage: false) };

        var answer = await StepAsync(model, "planCalls", new { site = Site(model) });

        var calls = Output(answer, "calls").EnumerateArray().ToList();
        calls.Should().HaveCount(2);
        calls.Single(call => call.GetProperty("source").GetString() == "NationalFloodPortal")
            .GetProperty("recordId").ValueKind.Should().Be(JsonValueKind.Null, "no record can be written for a call the model has none for");
        model.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Plan_calls_plans_nothing_for_a_source_that_resolves_onto_things_the_site_holds_none_of()
    {
        var model = new Model { Edges = [.. Edges(), new Edge("NationalFloodPortal", CoveringSourceResolver.ResolvesOntoPredicate, "Assessment")] };

        var answer = await StepAsync(model, "planCalls", new { site = Site(model) });

        Output(answer, "calls").EnumerateArray().Select(call => call.GetProperty("endpointName").GetString())
            .Should().Equal("OpenMeteoEndpoint");
    }

    [Theory]
    [InlineData("startAnalysis", "site")]
    [InlineData("offerSource", "source")]
    public async Task A_stamp_the_model_refuses_answers_a_failure_that_may_be_tried_again(string step, string port)
    {
        var model = new Model { FactsFail = true };
        var thing = port == "site" ? model["WillowBend"] : model["NationalFloodPortal"];

        var answer = await StepAsync(model, step, new Dictionary<string, object> { [port] = thing.ToString() });

        ResultOf(answer).Should().Be("fail");
        MayBeTriedAgain(answer).Should().BeTrue();
        model.Related.Should().BeEmpty("the analysis is started only once the site is stamped");
    }

    [Fact]
    public async Task Start_analysis_whose_relationship_the_model_refuses_answers_a_failure_that_may_be_tried_again()
    {
        var model = new Model { RelatingFails = true };

        var answer = await StepAsync(model, "startAnalysis", new { site = Site(model) });

        ResultOf(answer).Should().Be("fail");
        MayBeTriedAgain(answer).Should().BeTrue();
        answer.GetProperty("error").GetString().Should().Be("Could not start: balancesEnergy.");
        Output(answer, "started").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_step_handed_something_other_than_a_list_fails_saying_so()
    {
        var answer = await StepAsync(new Model(), "recordResults", new { resolved = "everything" });

        answer.GetProperty("error").GetString().Should().Be("A Forage step takes a list on its 'resolved' input.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("discoverEverything")]
    public async Task A_box_naming_no_step_forage_answers_fails_naming_the_steps(string? step)
    {
        var model = new Model();

        var answer = await StepAsync(model, step, new { site = Site(model) });

        ResultOf(answer).Should().Be("fail");
        answer.GetProperty("error").GetString().Should().Be(
            "A Forage box names its step in the 'step' setting, one of: resolveRegion, planCalls, recordResults, resolveWords, startAnalysis, offerSource.");
    }

    [Fact]
    public async Task A_step_handed_no_site_fails_saying_what_it_takes()
    {
        var answer = await StepAsync(new Model(), "planCalls", new { site = 42 });

        answer.GetProperty("error").GetString().Should().Be(
            "A Forage step takes the site on its 'site' input as a Thing: its id, or an object naming it by \"id\".");
    }

    [Fact]
    public async Task Each_step_declares_its_ports_and_its_step_setting()
    {
        await using var factory = new ForageWebApplicationFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        var manifest = await client.GetFromJsonAsync<JsonElement>("/manifest/planCalls");

        manifest.GetProperty("service").GetString().Should().Be("Forage plan calls");
        manifest.GetProperty("ports").EnumerateArray().Select(port => (port.GetProperty("portName").GetString(), port.GetProperty("direction").GetString()))
            .Should().Equal(("site", "in"), ("address", "in"), ("calls", "out"));
        var setting = manifest.GetProperty("settings").EnumerateArray().Should().ContainSingle().Subject;
        setting.GetProperty("name").GetString().Should().Be("step");
        setting.GetProperty("default").GetString().Should().Be("planCalls");
        (await client.GetAsync("/manifest/discoverEverything")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Tributary's place is taken by answering every planned call as fetched, which is what a direct run's fetches
    // answer here too.
    [Fact]
    public async Task The_steps_run_in_order_leave_the_model_as_a_direct_run_does()
    {
        var direct = new Model();
        await using (var factory = new ForageWebApplicationFactory())
        {
            await factory.InitializeAsync();
            factory.HandlerCallback = direct.Route;
            using var client = factory.CreateClient();
            await client.PostAsJsonAsync("/handle", new { subjectId = direct["WillowBend"] });
            await factory.CompleteStartedRuns();
        }

        var stepped = new Model { Ids = direct.Ids };
        var site = Site(stepped);
        var address = Output(await StepAsync(stepped, "resolveRegion", new { site }), "address");
        var calls = Output(await StepAsync(stepped, "planCalls", new { site, address }), "calls");
        var outcomes = calls.EnumerateArray().Select(call => new { call, written = (object?)null }).ToArray();
        var results = await StepAsync(stepped, "recordResults", new { resolved = outcomes });
        await StepAsync(stepped, "resolveWords", new { resolved = Output(results, "resolved") });
        await StepAsync(stepped, "startAnalysis", new { site });

        direct.Fetched.Should().HaveCount(calls.GetArrayLength());
        string.Join(" ", direct.Fetched).Should().Contain("NationalFloodPortalEndpoint").And.Contain("OpenMeteoEndpoint");
        calls.EnumerateArray().Select(call => call.GetProperty("endpointName").GetString())
            .Should().BeEquivalentTo("NationalFloodPortalEndpoint", "OpenMeteoEndpoint");
        direct.Records.Should().NotBeEmpty();
        direct.Related.Should().NotBeEmpty();
        stepped.Records.Should().BeEquivalentTo(direct.Records);
        stepped.Facts.Should().BeEquivalentTo(direct.Facts);
        stepped.Related.Should().BeEquivalentTo(direct.Related);
    }
}
