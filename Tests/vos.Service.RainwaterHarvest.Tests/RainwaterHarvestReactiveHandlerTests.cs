using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.RainwaterHarvest.Services;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.RainwaterHarvest.Tests;

// What the handler does around the arithmetic: which read it makes, what it writes and onto what, and
// which changes it asks to be woken for.
public class RainwaterHarvestReactiveHandlerTests
{
    private static readonly Guid Study = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // 8.88 ha of hard surface under 700 mm at 0.8 runoff captures 49,728 m³ against 58,400 m³ of demand —
    // 85.15%, of which 17,600 m³ is drunk and 40,800 m³ irrigated.
    private const string WillowBend = """
        { "builtFootprintHectares": { "Value": 8.88 }, "rainfallMillimetresPerYear": { "Value": 700 },
          "runoffCoefficient": { "Value": 0.8 }, "population": { "Value": 320 },
          "perCapitaConsumptionM3": { "Value": 55 }, "productiveFootprintHectares": { "Value": 8.16 },
          "irrigationDemandM3PerHectarePerYear": { "Value": 5000 } }
        """;

    private static RainwaterHarvestReactiveHandler NewHandler(RecordingHttpMessageHandler http) =>
        new(new TestHttpClientFactory(new HttpClient(http)), NullLogger<RainwaterHarvestReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

    private static RecordingHttpMessageHandler Serving(string properties) =>
        new(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(properties, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK));

    [Fact]
    public async Task Every_output_is_written_back_onto_the_study_as_a_fact()
    {
        var http = Serving(WillowBend);

        var outputs = await NewHandler(http).RecomputeAsync(Study);

        outputs.HarvestM3PerYear.Should().BeApproximately(49728, 1e-6);
        foreach (var output in new[]
                 {
                     RainwaterHarvestReactiveHandler.HarvestOutput,
                     RainwaterHarvestReactiveHandler.DomesticDemandOutput,
                     RainwaterHarvestReactiveHandler.IrrigationDemandOutput,
                     RainwaterHarvestReactiveHandler.TotalWaterDemandOutput,
                     RainwaterHarvestReactiveHandler.PctOfWaterDemandOutput,
                 })
            http.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Post
                && request.Uri == $"http://mycelium/api/things/{Study}/properties/{output}/facts");
    }

    [Fact]
    public async Task The_two_demand_components_are_written_separately_and_not_folded_into_the_total()
    {
        var http = Serving(WillowBend);

        await NewHandler(http).RecomputeAsync(Study);

        http.Requests.Single(request => request.Uri.Contains(RainwaterHarvestReactiveHandler.DomesticDemandOutput))
            .Body.Should().Contain("17600");
        http.Requests.Single(request => request.Uri.Contains(RainwaterHarvestReactiveHandler.IrrigationDemandOutput))
            .Body.Should().Contain("40800");
        http.Requests.Single(request => request.Uri.Contains(RainwaterHarvestReactiveHandler.TotalWaterDemandOutput))
            .Body.Should().Contain("58400");
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

    [Fact]
    public async Task An_input_the_study_does_not_carry_is_refused_by_name()
    {
        var http = Serving("""
            { "builtFootprintHectares": { "Value": 8.88 }, "runoffCoefficient": { "Value": 0.8 },
              "population": { "Value": 320 }, "perCapitaConsumptionM3": { "Value": 55 },
              "productiveFootprintHectares": { "Value": 8.16 },
              "irrigationDemandM3PerHectarePerYear": { "Value": 5000 } }
            """);

        var refusal = await Assert.ThrowsAsync<KeyNotFoundException>(() => NewHandler(http).RecomputeAsync(Study));

        refusal.Message.Should().Contain("rainfallMillimetresPerYear").And.Contain("RainwaterHarvest");
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

        failure.Message.Should().Contain(RainwaterHarvestReactiveHandler.HarvestOutput);
    }

    // Both footprints are land allocation's outputs, so a re-run of that service carries through to this
    // balance on its own. None of this service's own outputs may be in the set: it writes all five onto
    // the study it watches, and watching them would recompute forever.
    [Fact]
    public void A_footprint_wakes_a_recompute_and_this_service_s_own_outputs_do_not()
    {
        RainwaterHarvestReactiveHandler.InputProperties.Should().BeEquivalentTo(
        [
            "builtFootprintHectares", "productiveFootprintHectares", "rainfallMillimetresPerYear",
            "runoffCoefficient", "population", "perCapitaConsumptionM3",
            "irrigationDemandM3PerHectarePerYear",
        ]);
    }
}
