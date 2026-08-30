using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.WaterReserve.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.WaterReserve.Tests;

// Story #5839 — the reactive (model-driven) water analysis: read inputs off the anchor, compute, write outputs back.
public class WaterReserveReactiveHandlerTests
{
    private static readonly Guid Anchor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // population 300 * perCapita 50 = 15000 annual; storage 900 → daysOfSupply = 900 / (15000/365) ≈ 21.9 days.
    private const string AnchorInputs = """
        { "population": { "Value": 300 }, "perCapitaConsumptionM3": { "Value": 50 },
          "storageCapacityM3": { "Value": 900 } }
        """;

    private static WaterReserveReactiveHandler NewHandler(
        RecordingHttpMessageHandler handler, ILogger<WaterReserveReactiveHandler>? logger = null) =>
        new(new TestHttpClientFactory(new HttpClient(handler)),
            logger ?? NullLogger<WaterReserveReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

    // #6549 made the refusal name its input; 6826 waits instead of refusing. A withheld number and an
    // absent one say the same thing about a study — the figure has not arrived — so the name reaches the
    // log either way rather than one of the two failing the dispatch.
    [Fact]
    public async Task A_number_the_study_withholds_is_waited_for_by_name()
    {
        var withNull = AnchorInputs.Replace(
            "\"storageCapacityM3\": { \"Value\": 900 }",
            "\"storageCapacityM3\": { \"Value\": null }");
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(withNull, Encoding.UTF8, "application/json"),
        });
        var logger = new CapturingLogger<WaterReserveReactiveHandler>();

        var answer = await NewHandler(handler, logger).RecomputeAsync(Anchor);

        Assert.Null(answer.Outputs);
        Assert.Equal(new[] { "storageCapacityM3" }, answer.WaitingFor);
        Assert.Contains(logger.Lines, line =>
            line.Contains("storageCapacityM3") && line.Contains(Anchor.ToString()));
    }

    [Fact]
    public async Task Reads_the_anchor_and_computes_days_of_supply()
    {
        var handler = new RecordingHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnchorInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var answer = await NewHandler(handler).RecomputeAsync(Anchor);

        Assert.Equal(21.9, answer.Outputs!.DaysOfSupply, 1);
        Assert.Empty(answer.WaitingFor);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get && r.Uri.Contains($"/api/things/{Anchor}/properties"));
    }

    // All four figures are declared as expressions on the shared study archetype, so the model works them
    // out and a derived property refuses every value write. A write left here would throw on the first and
    // abandon the rest, which is why this service now asserts nothing at all.
    [Theory]
    [InlineData("daysOfSupply")]
    [InlineData("emergencyReserveM3")]
    [InlineData("annualConsumptionM3")]
    [InlineData("pctAnnualConsumption")]
    public async Task It_asserts_no_figure_the_model_derives_for_itself(string derived)
    {
        var handler = new RecordingHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnchorInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        await NewHandler(handler).RecomputeAsync(Anchor);

        Assert.DoesNotContain(handler.Requests,
            r => r.Method == HttpMethod.Post && r.Uri.Contains($"/properties/{derived}/facts"));
    }

    // A string-valued input goes through double.TryParse rather than GetDouble. A regional format
    // that writes 900,5 must not turn "900.5" into 9005 — the values come from the model, not from
    // a person, so the dot is always a decimal point.
    [Fact]
    public async Task String_valued_inputs_parse_the_same_whatever_the_regional_format()
    {
        const string stringInputs = """
            { "population": { "Value": "300" }, "perCapitaConsumptionM3": { "Value": "50" },
              "storageCapacityM3": { "Value": "900.5" } }
            """;
        var handler = new RecordingHttpMessageHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stringInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var answer = await TestCulture.InAsync(TestCulture.CommaDecimal, () => NewHandler(handler).RecomputeAsync(Anchor));

        Assert.Equal(21.9, answer.Outputs!.DaysOfSupply, 1);
    }

    // Bug 6826: a study a submission built carries land and a programme and no reservoir, so the capacity
    // is absent until a building model exists. Throwing on it had the broker record the dispatch failed
    // and re-drive it on every reconciliation for as long as the model lived.
    [Fact]
    public async Task An_input_the_study_does_not_carry_is_waited_for_rather_than_failing_the_dispatch()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "population": { "Value": 300 } }""", Encoding.UTF8, "application/json"),
        });
        var logger = new CapturingLogger<WaterReserveReactiveHandler>();

        var answer = await NewHandler(handler, logger).RecomputeAsync(Anchor);

        Assert.Null(answer.Outputs);
        Assert.Equal(new[] { "perCapitaConsumptionM3", "storageCapacityM3" }, answer.WaitingFor);
        Assert.Contains(logger.Lines, line =>
            line.Contains("perCapitaConsumptionM3") && line.Contains("storageCapacityM3"));
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }
}
