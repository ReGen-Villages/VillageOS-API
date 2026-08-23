using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.EnergyBalance.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.EnergyBalance.Tests;

// Story #5839 — the reactive (model-driven) energy analysis: read inputs off the anchor, compute, write outputs back.
public class EnergyBalanceReactiveHandlerTests
{
    private static readonly Guid Anchor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // area 13500 * resource 1600 * eff 0.2 / 1000 = 4320 solar; + 100 other = 4420 total; / 4000 consumption * 100 = 110.5%.
    private const string AnchorInputs = """
        { "solarPvAreaM2": { "Value": 13500 }, "solarResourceKwhPerM2PerYear": { "Value": 1600 },
          "moduleEfficiency": { "Value": 0.2 }, "performanceRatio": { "Value": 1.0 }, "otherGenerationMwhPerYear": { "Value": 100 },
          "annualConsumptionMwhPerYear": { "Value": 4000 } }
        """;

    // The three figures the model derives itself are no longer written here: the shared analysis declares
    // them as expressions, and a derived property refuses every value write. Writing one would throw on
    // the first call and abandon the rest of the recompute, so what is left is the one output an
    // expression cannot hold — a boolean.
    [Fact]
    public async Task It_writes_no_figure_the_model_derives_for_itself()
    {
        var handler = new RecordingHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnchorInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        await reactive.RecomputeAsync(Anchor);

        foreach (var derived in new[] { "pctOfConsumption", "solarGenerationMwhPerYear", "totalGenerationMwhPerYear" })
            Assert.DoesNotContain(handler.Requests,
                r => r.Method == HttpMethod.Post && r.Uri.Contains($"/properties/{derived}/facts"));
    }

    [Fact]
    public async Task Reads_inputs_computes_and_writes_outputs_onto_the_anchor()
    {
        var handler = new RecordingHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnchorInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        var outputs = await reactive.RecomputeAsync(Anchor);

        Assert.Equal(110.5, outputs.PctOfConsumption, 3);
        Assert.True(outputs.NetPositive);

        // It read the anchor and wrote the verdict back onto it.
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get && r.Uri.Contains($"/api/things/{Anchor}/properties"));
        var verdictWrite = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/properties/netPositive/facts"));
        Assert.Contains("true", verdictWrite.Body);
    }

    // A string-valued input goes through double.TryParse rather than GetDouble. A regional format
    // that writes 0,2 must not turn "0.2" into 2 — the values come from the model, not from a
    // person, so the dot is always a decimal point.
    [Fact]
    public async Task String_valued_inputs_parse_the_same_whatever_the_regional_format()
    {
        const string stringInputs = """
            { "solarPvAreaM2": { "Value": "13500" }, "solarResourceKwhPerM2PerYear": { "Value": "1600" },
              "moduleEfficiency": { "Value": "0.2" }, "performanceRatio": { "Value": "1.0" },
              "otherGenerationMwhPerYear": { "Value": "100" }, "annualConsumptionMwhPerYear": { "Value": "4000" } }
            """;
        var handler = new RecordingHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stringInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        var outputs = await TestCulture.InAsync(TestCulture.CommaDecimal, () => reactive.RecomputeAsync(Anchor));

        Assert.Equal(110.5, outputs.PctOfConsumption, 3);
    }

    // #6549: the platform withholds a roll-up whose member type resolves to nothing rather than answering
    // zero, so a required input can now arrive present-but-null. The refusal has to say which one — six
    // inputs are read here, and "not numeric: Null" names none of them.
    [Fact]
    public async Task Refusing_a_null_input_names_which_input_it_was()
    {
        var withNullRollup = AnchorInputs.Replace(
            "\"otherGenerationMwhPerYear\": { \"Value\": 100 }",
            "\"otherGenerationMwhPerYear\": { \"Value\": null }");
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(withNullRollup, Encoding.UTF8, "application/json"),
        });
        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => reactive.RecomputeAsync(Anchor));

        Assert.Contains("otherGenerationMwhPerYear", thrown.Message);
        // Present-with-no-value has its own message: it says the input is on the study but empty, and names
        // the platform behaviour that empties it, so an operator is not left reading "not numeric: Null".
        Assert.Contains("no value", thrown.Message);
        Assert.Contains("roll-up", thrown.Message);
    }

    [Fact]
    public async Task Refusing_a_non_numeric_input_names_which_input_it_was()
    {
        var withText = AnchorInputs.Replace(
            "\"annualConsumptionMwhPerYear\": { \"Value\": 4000 }",
            "\"annualConsumptionMwhPerYear\": { \"Value\": \"unmeasured\" }");
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(withText, Encoding.UTF8, "application/json"),
        });
        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => reactive.RecomputeAsync(Anchor));

        Assert.Contains("annualConsumptionMwhPerYear", thrown.Message);
    }

    [Fact]
    public async Task Fails_when_a_required_input_is_missing_from_the_anchor()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "moduleEfficiency": { "Value": 0.2 } }""", Encoding.UTF8, "application/json"),
        });
        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => reactive.RecomputeAsync(Anchor));
    }
}
