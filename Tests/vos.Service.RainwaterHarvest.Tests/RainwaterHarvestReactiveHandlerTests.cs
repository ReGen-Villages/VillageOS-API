using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.RainwaterHarvest.Services;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.RainwaterHarvest.Tests;

// What the handler does around the arithmetic: which reads it makes, what it writes and onto which
// property, and which changes it asks to be woken for. The demands come from the model, so the properties
// it reads and writes are the model's answer and not a list held here.
public class RainwaterHarvestReactiveHandlerTests
{
    private static readonly Guid Study = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Archetype = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Domestic = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid Irrigation = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // 8.88 ha of hard surface under 700 mm at 0.8 runoff captures 49,728 m³. Drinking water takes the
    // 17,600 m³ it needs and irrigation takes the 32,128 m³ left, against the 40,800 m³ it wanted.
    private const string WillowBend = """
        { "builtFootprintHectares": { "Value": 8.88 }, "rainfallMillimetresPerYear": { "Value": 700 },
          "runoffCoefficient": { "Value": 0.8 }, "population": { "Value": 320 },
          "perCapitaConsumptionM3": { "Value": 55 }, "productiveFootprintHectares": { "Value": 8.16 },
          "irrigationDemandM3PerHectarePerYear": { "Value": 5000 } }
        """;

    private static SnapshotProperty Value(object value) =>
        new(JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement, null, null);

    private static SnapshotThing DemandComponent(
        Guid id, string name, long servingOrder,
        string quantity, string rate, string demand, string coverage, string shortfall) =>
        new(id, name, false,
            new Dictionary<string, SnapshotProperty>
            {
                ["servingOrder"] = Value(servingOrder),
                ["demandQuantityProperty"] = Value(quantity),
                ["demandRateProperty"] = Value(rate),
                ["demandProperty"] = Value(demand),
                ["coverageProperty"] = Value(coverage),
                ["shortfallProperty"] = Value(shortfall),
            },
            new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotThing TheArchetype => new(
        Archetype, "WaterDemandComponent", true,
        new Dictionary<string, SnapshotProperty>
        {
            [WaterDemandComponentReader.ComponentArchetypeFlag] = Value(true),
        },
        new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotDocument TheShippedVocabulary => new(0,
        [
            TheArchetype,
            DemandComponent(Domestic, "domestic-demand", 1, "population", "perCapitaConsumptionM3",
                "domesticDemandM3PerYear", "pctOfDomesticDemand", "domesticShortfallM3PerYear"),
            DemandComponent(Irrigation, "irrigation-demand", 2,
                "productiveFootprintHectares", "irrigationDemandM3PerHectarePerYear",
                "irrigationDemandM3PerYear", "pctOfIrrigationDemand", "irrigationShortfallM3PerYear"),
        ], []);

    private sealed class StubSubscriptions(SnapshotDocument snapshot) : ISubscriptionClient
    {
        public bool Released { get; private set; }
        public bool FailRelease { get; set; }

        public Task<SubscribeResult> SubscribeAsync(SubscriptionSelector selector, CancellationToken ct = default) =>
            Task.FromResult(new SubscribeResult(Guid.NewGuid(), 0, snapshot));

        public Task UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            if (FailRelease) throw new HttpRequestException("mycelium is already gone");
            Released = true;
            return Task.CompletedTask;
        }

        public Task<AddObjectsResult> AddObjectsAsync(Guid id, SubscriptionSelector s, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task RemoveObjectsAsync(Guid id, IEnumerable<Guid> objectIds, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<ModelChangeEvent> StreamAsync(Guid id, long from, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public event Action? Reconnected { add { } remove { } }
    }

    private static RainwaterHarvestReactiveHandler NewHandler(
        RecordingHttpMessageHandler http, SnapshotDocument? vocabulary = null,
        ILogger<RainwaterHarvestReactiveHandler>? logger = null) =>
        new(new TestHttpClientFactory(new HttpClient(http)),
            logger ?? NullLogger<RainwaterHarvestReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token",
            new StubSubscriptions(vocabulary ?? TheShippedVocabulary));

    private static RecordingHttpMessageHandler Serving(string properties) =>
        new(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(properties, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK));

    /// <summary>The figure written onto one named property, read back as a number: the Fact body carries a
    /// double, and matching its text would pin the last digits of an arithmetic that has none to spare.</summary>
    private static double WrittenTo(RecordingHttpMessageHandler http, string property) =>
        JsonDocument.Parse(http.Requests.Single(request => request.Method == HttpMethod.Post
                && request.Uri == $"http://mycelium/api/things/{Study}/properties/{property}/facts").Body)
            .RootElement.GetProperty("value").GetDouble();

    [Fact]
    public async Task Every_output_is_written_back_onto_the_study_as_a_fact()
    {
        var http = Serving(WillowBend);

        var answer = await NewHandler(http).RecomputeAsync(Study);

        answer.Outputs!.HarvestM3PerYear.Should().BeApproximately(49728, 1e-6);
        foreach (var output in new[]
                 {
                     RainwaterHarvestReactiveHandler.TotalWaterDemandOutput,
                     "domesticDemandM3PerYear", "pctOfDomesticDemand", "domesticShortfallM3PerYear",
                     "irrigationDemandM3PerYear", "pctOfIrrigationDemand", "irrigationShortfallM3PerYear",
                 })
            http.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Post
                && request.Uri == $"http://mycelium/api/things/{Study}/properties/{output}/facts");
    }

    // The harvest volume and the coverage of the whole demand are declared as expressions on the shared
    // study archetype, so the model works them out and a derived property refuses every value write. What
    // stays here is what is worked out across the set of demands the model declares — each demand's size,
    // the total, and the apportionment in serving order — which is a reduction's shape, not an
    // expression's.
    [Theory]
    [InlineData("harvestM3PerYear")]
    [InlineData("pctOfWaterDemand")]
    public async Task It_asserts_no_figure_the_model_derives_for_itself(string derived)
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        http.Requests.Should().NotContain(request => request.Method == HttpMethod.Post
            && request.Uri == $"http://mycelium/api/things/{Study}/properties/{derived}/facts");
    }

    // Each answer lands on the property its own demand names, not on one the handler chose. A pair
    // swapped here would report the irrigation gap as the drinking-water gap, which is the reading a
    // planner would act on first.
    [Fact]
    public async Task Each_demand_writes_its_answers_onto_the_properties_it_names()
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        WrittenTo(http, "domesticDemandM3PerYear").Should().BeApproximately(17600, 1e-9);
        WrittenTo(http, "pctOfDomesticDemand").Should().BeApproximately(100, 1e-9);
        WrittenTo(http, "domesticShortfallM3PerYear").Should().Be(0);

        WrittenTo(http, "irrigationDemandM3PerYear").Should().BeApproximately(40800, 1e-9);
        WrittenTo(http, "pctOfIrrigationDemand").Should().BeApproximately(78.7450980392, 1e-9);
        WrittenTo(http, "irrigationShortfallM3PerYear").Should().BeApproximately(8672, 1e-9);
    }

    // The runoff coefficient and the irrigation rate are judgements about surfaces and about growing, not
    // about this place, so they are declared on the shared study archetype and no study owns one. Reading
    // the effective properties is what makes them arrive anyway.
    [Fact]
    public async Task The_read_is_of_the_study_s_effective_properties_so_an_inherited_assumption_arrives()
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        http.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Get
            && request.Uri == $"http://mycelium{MyceliumRoutes.ThingProperties(Study)}");
    }

    // The same worked example with every figure written as text, which goes through a parse rather than
    // straight off the JSON number. TestCulture explains what a comma-decimal region does to it.
    [Fact]
    public async Task A_string_valued_input_reads_the_same_whatever_the_regional_format()
    {
        var http = Serving("""
            { "builtFootprintHectares": { "Value": "8.88" }, "rainfallMillimetresPerYear": { "Value": "700" },
              "runoffCoefficient": { "Value": "0.8" }, "population": { "Value": "320" },
              "perCapitaConsumptionM3": { "Value": "55" }, "productiveFootprintHectares": { "Value": "8.16" },
              "irrigationDemandM3PerHectarePerYear": { "Value": "5000" } }
            """);

        var answer = await TestCulture.InAsync(
            TestCulture.CommaDecimal, () => NewHandler(http).RecomputeAsync(Study));

        answer.Outputs!.HarvestM3PerYear.Should().BeApproximately(49728, 1e-6);
        answer.Outputs.PctOfWaterDemand.Should().BeApproximately(85.150684931, 1e-9);
    }

    // Bug 6826: rainfall arrives from open-data discovery after the analysis is dispatched, so a study
    // carries none at first. Throwing on it had the broker record the dispatch failed and re-drive it on
    // every reconciliation for as long as the model lived.
    [Fact]
    public async Task An_input_the_study_does_not_carry_is_waited_for_by_name()
    {
        var http = Serving("""
            { "builtFootprintHectares": { "Value": 8.88 }, "runoffCoefficient": { "Value": 0.8 },
              "population": { "Value": 320 }, "perCapitaConsumptionM3": { "Value": 55 },
              "productiveFootprintHectares": { "Value": 8.16 },
              "irrigationDemandM3PerHectarePerYear": { "Value": 5000 } }
            """);
        var logger = new CapturingLogger<RainwaterHarvestReactiveHandler>();

        var answer = await NewHandler(http, logger: logger).RecomputeAsync(Study);

        answer.Outputs.Should().BeNull();
        answer.WaitingFor.Should().Equal("rainfallMillimetresPerYear");
        logger.Lines.Should().Contain(line =>
            line.Contains("RainwaterHarvest") && line.Contains("rainfallMillimetresPerYear"));
        http.Requests.Should().NotContain(request => request.Method == HttpMethod.Post);
    }

    // A demand the model sizes by a property no study carries is waited for the same way, and the name
    // that reaches the answer is the model's rather than one compiled here.
    [Fact]
    public async Task A_demand_sized_by_a_property_the_study_does_not_carry_is_waited_for_by_that_name()
    {
        var http = Serving(WillowBend);
        var vocabulary = new SnapshotDocument(0,
            [
                TheArchetype,
                DemandComponent(Domestic, "domestic-demand", 1, "residentsOnSite", "perCapitaConsumptionM3",
                    "domesticDemandM3PerYear", "pctOfDomesticDemand", "domesticShortfallM3PerYear"),
            ], []);

        var answer = await NewHandler(http, vocabulary).RecomputeAsync(Study);

        answer.Outputs.Should().BeNull();
        answer.WaitingFor.Should().Equal("residentsOnSite");
        http.Requests.Should().NotContain(request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_study_the_broker_will_not_hand_over_is_raised_naming_the_study()
    {
        var http = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => NewHandler(http).RecomputeAsync(Study));

        failure.Message.Should().Contain(Study.ToString()).And.Contain("RainwaterHarvest");
    }

    [Fact]
    public async Task A_refused_write_is_raised_rather_than_reported_as_a_computed_study()
    {
        var http = new RecordingHttpMessageHandler(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(WillowBend, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => NewHandler(http).RecomputeAsync(Study));

        failure.Message.Should().Contain(RainwaterHarvestReactiveHandler.TotalWaterDemandOutput);
    }

    // What wakes this service is the three the harvest volume is worked out from plus whatever properties
    // the model's demands are sized by. A set fixed in the service would leave a study stale the moment a
    // demand named a property that set had never heard of.
    [Fact]
    public async Task What_wakes_it_grows_to_cover_the_properties_the_model_sizes_its_demands_by()
    {
        var http = Serving(WillowBend);
        var handler = NewHandler(http);

        handler.WatchedProperties.Should().BeEquivalentTo(RainwaterHarvestReactiveHandler.InputProperties);

        await handler.RecomputeAsync(Study);

        handler.WatchedProperties.Should().BeEquivalentTo(new[]
        {
            "builtFootprintHectares", "rainfallMillimetresPerYear", "runoffCoefficient",
            "population", "perCapitaConsumptionM3",
            "productiveFootprintHectares", "irrigationDemandM3PerHectarePerYear",
        });
    }

    // What the demands are sized by is learned from the model, so a study waiting on one of those figures
    // is woken when it lands only if that name is already watched. The set is therefore recorded before the
    // study is read rather than after it is computed.
    [Fact]
    public async Task A_study_it_is_still_waiting_on_widens_what_wakes_it_all_the_same()
    {
        var http = Serving("""
            { "builtFootprintHectares": { "Value": 8.88 }, "rainfallMillimetresPerYear": { "Value": 700 },
              "runoffCoefficient": { "Value": 0.8 } }
            """);
        var handler = NewHandler(http);

        var answer = await handler.RecomputeAsync(Study);

        answer.Outputs.Should().BeNull();
        handler.WatchedProperties.Should().Contain("population")
            .And.Contain("irrigationDemandM3PerHectarePerYear");
    }

    // A demand whose answer lands on something this service wakes on would recompute the study for as long
    // as the model held that spelling. Refused before the first write, because the loop leaves nothing
    // behind saying which demand caused it — and refused against the whole watched set, so one demand's
    // answer landing on another's quantity is caught too.
    [Theory]
    [InlineData("runoffCoefficient")]
    [InlineData("productiveFootprintHectares")]
    public async Task A_demand_writing_onto_something_it_wakes_on_is_refused_before_anything_is_written(
        string written)
    {
        var http = Serving(WillowBend);
        var vocabulary = new SnapshotDocument(0,
            [
                TheArchetype,
                DemandComponent(Domestic, "domestic-demand", 1, "population", "perCapitaConsumptionM3",
                    "domesticDemandM3PerYear", written, "domesticShortfallM3PerYear"),
                DemandComponent(Irrigation, "irrigation-demand", 2,
                    "productiveFootprintHectares", "irrigationDemandM3PerHectarePerYear",
                    "irrigationDemandM3PerYear", "pctOfIrrigationDemand", "irrigationShortfallM3PerYear"),
            ], []);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewHandler(http, vocabulary).RecomputeAsync(Study));

        refusal.Message.Should().Contain("domestic-demand").And.Contain(written);
        http.Requests.Should().NotContain(request => request.Method == HttpMethod.Post);
    }

    private static RainwaterHarvestReactiveHandler NewHandler(
        RecordingHttpMessageHandler http, StubSubscriptions subscriptions) =>
        new(new TestHttpClientFactory(new HttpClient(http)), NullLogger<RainwaterHarvestReactiveHandler>.Instance,
            "http://mycelium", "test-token", subscriptions);

    [Fact]
    public async Task The_subscription_it_read_the_demands_through_is_released()
    {
        var subscriptions = new StubSubscriptions(TheShippedVocabulary);

        await NewHandler(Serving(WillowBend), subscriptions).RecomputeAsync(Study);

        subscriptions.Released.Should().BeTrue();
    }

    [Fact]
    public async Task The_subscription_is_released_even_when_the_demands_cannot_be_read()
    {
        var subscriptions = new StubSubscriptions(new SnapshotDocument(0, [TheArchetype], []));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewHandler(Serving(WillowBend), subscriptions).RecomputeAsync(Study));

        subscriptions.Released.Should().BeTrue("a read that fails still holds a subscription open");
    }

    // The harvest is computed and written before the release is even attempted, so a broker that has
    // already gone must not turn a study that was answered into a study that failed.
    [Fact]
    public async Task A_recompute_that_succeeded_is_not_lost_because_the_subscription_could_not_be_released()
    {
        var http = Serving(WillowBend);
        var subscriptions = new StubSubscriptions(TheShippedVocabulary) { FailRelease = true };

        var answer = await NewHandler(http, subscriptions).RecomputeAsync(Study);

        answer.Outputs!.HarvestM3PerYear.Should().BeApproximately(49728, 1e-6);
        http.Requests.Should().Contain(request => request.Method == HttpMethod.Post);
    }

    // Through the constructor the service itself uses, where every test above hands in a subscription
    // client instead. Until it has read a model it wakes only on what it reads itself, which is the set a
    // follower is registered with before the first dispatch arrives.
    [Fact]
    public void A_handler_that_has_not_computed_yet_wakes_only_on_what_it_reads_itself()
    {
        var handler = new RainwaterHarvestReactiveHandler(
            new TestHttpClientFactory(new HttpClient()),
            NullLogger<RainwaterHarvestReactiveHandler>.Instance, "http://mycelium", "test-token");

        handler.WatchedProperties.Should().BeEquivalentTo(RainwaterHarvestReactiveHandler.InputProperties);
    }

    // It writes the total onto the study it watches, so a set holding it would recompute forever rather
    // than compute a wrong number. The rest of what it writes is named by the model, and the refusal above
    // is what covers those.
    [Fact]
    public void None_of_the_outputs_it_names_itself_can_wake_it()
    {
        RainwaterHarvestReactiveHandler.InputProperties.Should()
            .NotIntersectWith(DeclaredOutputs.Of<RainwaterHarvestReactiveHandler>());
    }
}
