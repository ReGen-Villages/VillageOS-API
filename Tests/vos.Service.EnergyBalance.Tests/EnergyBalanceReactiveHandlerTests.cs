using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
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

    private static EnergyBalanceReactiveHandler NewHandler(
        RecordingHttpMessageHandler http, ILogger<EnergyBalanceReactiveHandler>? logger = null) =>
        new(new TestHttpClientFactory(new HttpClient(http)),
            logger ?? NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

    private static RecordingHttpMessageHandler Serving(string properties) =>
        new(request => request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(properties, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK));

    // The three figures the model derives itself are no longer written here: the shared analysis declares
    // them as expressions, and a derived property refuses every value write. Writing one would throw on
    // the first call and abandon the rest of the recompute, so what is left is the one output an
    // expression cannot hold — a boolean.
    [Fact]
    public async Task It_writes_no_figure_the_model_derives_for_itself()
    {
        var http = Serving(AnchorInputs);

        await NewHandler(http).RecomputeAsync(Anchor);

        foreach (var derived in new[] { "pctOfConsumption", "solarGenerationMwhPerYear", "totalGenerationMwhPerYear" })
            Assert.DoesNotContain(http.Requests,
                r => r.Method == HttpMethod.Post && r.Uri.Contains($"/properties/{derived}/facts"));
    }

    [Fact]
    public async Task Reads_inputs_computes_and_writes_outputs_onto_the_anchor()
    {
        var http = Serving(AnchorInputs);

        var answer = await NewHandler(http).RecomputeAsync(Anchor);

        Assert.Equal(110.5, answer.Outputs!.PctOfConsumption, 3);
        Assert.True(answer.Outputs.NetPositive);
        Assert.Empty(answer.WaitingFor);

        // It read the anchor and wrote the verdict back onto it.
        Assert.Contains(http.Requests, r => r.Method == HttpMethod.Get && r.Uri.Contains($"/api/things/{Anchor}/properties"));
        var verdictWrite = Assert.Single(http.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/properties/netPositive/facts"));
        Assert.Contains("true", verdictWrite.Body);
    }

    // A string-valued input goes through double.TryParse rather than GetDouble. A regional format
    // that writes 0,2 must not turn "0.2" into 2 — the values come from the model, not from a
    // person, so the dot is always a decimal point.
    [Fact]
    public async Task String_valued_inputs_parse_the_same_whatever_the_regional_format()
    {
        var http = Serving("""
            { "solarPvAreaM2": { "Value": "13500" }, "solarResourceKwhPerM2PerYear": { "Value": "1600" },
              "moduleEfficiency": { "Value": "0.2" }, "performanceRatio": { "Value": "1.0" },
              "otherGenerationMwhPerYear": { "Value": "100" }, "annualConsumptionMwhPerYear": { "Value": "4000" } }
            """);

        var answer = await TestCulture.InAsync(TestCulture.CommaDecimal, () => NewHandler(http).RecomputeAsync(Anchor));

        Assert.Equal(110.5, answer.Outputs!.PctOfConsumption, 3);
    }

    // #6549: the platform withholds a roll-up whose member type resolves to nothing rather than answering
    // zero, so an input can arrive present-but-null. 6826: that says the same thing about a study as an
    // absent one — no Thing to reduce over yet — so it is waited for, and the name still has to reach the
    // log. Six inputs are read here, and "not numeric: Null" names none of them.
    [Fact]
    public async Task A_number_the_study_withholds_is_waited_for_by_name()
    {
        var http = Serving(AnchorInputs.Replace(
            "\"otherGenerationMwhPerYear\": { \"Value\": 100 }",
            "\"otherGenerationMwhPerYear\": { \"Value\": null }"));
        var logger = new CapturingLogger<EnergyBalanceReactiveHandler>();

        var answer = await NewHandler(http, logger).RecomputeAsync(Anchor);

        Assert.Null(answer.Outputs);
        Assert.Equal(new[] { "otherGenerationMwhPerYear" }, answer.WaitingFor);
        Assert.Contains(logger.Lines, line =>
            line.Contains("EnergyBalance") && line.Contains("otherGenerationMwhPerYear"));
    }

    // Text where a number belongs is a fault in the model rather than a figure still to arrive, so it is
    // refused rather than waited for — and the refusal says which of the six it was.
    [Fact]
    public async Task Refusing_a_non_numeric_input_names_which_input_it_was()
    {
        var http = Serving(AnchorInputs.Replace(
            "\"annualConsumptionMwhPerYear\": { \"Value\": 4000 }",
            "\"annualConsumptionMwhPerYear\": { \"Value\": \"unmeasured\" }"));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => NewHandler(http).RecomputeAsync(Anchor));

        Assert.Contains("annualConsumptionMwhPerYear", thrown.Message);
    }

    // Bug 6826: a study a submission built describes land and a programme and carries no panels, so the
    // area is absent until a building model exists. Throwing on it had the broker record the dispatch
    // failed and re-drive it on every reconciliation for as long as the model lived.
    [Fact]
    public async Task An_input_the_study_does_not_carry_is_waited_for_and_no_verdict_is_written()
    {
        var http = Serving("""{ "moduleEfficiency": { "Value": 0.2 } }""");
        var logger = new CapturingLogger<EnergyBalanceReactiveHandler>();

        var answer = await NewHandler(http, logger).RecomputeAsync(Anchor);

        Assert.Null(answer.Outputs);
        Assert.Contains("solarPvAreaM2", answer.WaitingFor);
        Assert.Contains(logger.Lines, line => line.Contains("EnergyBalance") && line.Contains("solarPvAreaM2"));
        Assert.DoesNotContain(http.Requests, r => r.Method == HttpMethod.Post);
    }
}
