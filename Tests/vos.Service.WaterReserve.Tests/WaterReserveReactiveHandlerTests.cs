using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.WaterReserve.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.WaterReserve.Tests;

// Story #5839 — the reactive (model-driven) water analysis: read inputs off the anchor, compute, write outputs back.
public class WaterReserveReactiveHandlerTests
{
    private static readonly Guid Anchor = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed record Recorded(HttpMethod Method, string Uri, string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public readonly List<Recorded> Requests = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Recorded(request.Method, request.RequestUri!.ToString(), body));
            return responder(request);
        }
    }

    // population 300 * perCapita 50 = 15000 annual; storage 900 → daysOfSupply = 900 / (15000/365) ≈ 21.9 days.
    private const string AnchorInputs = """
        { "population": { "Value": 300 }, "perCapitaConsumptionM3": { "Value": 50 },
          "storageCapacityM3": { "Value": 900 } }
        """;

    private static WaterReserveReactiveHandler NewHandler(RecordingHandler handler) =>
        new(new TestHttpClientFactory(new HttpClient(handler)), NullLogger<WaterReserveReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

    // #6549: the same reader serves both services now, so the refusal names its input here too.
    [Fact]
    public async Task Refusing_a_null_input_names_which_input_it_was()
    {
        var withNull = AnchorInputs.Replace(
            "\"storageCapacityM3\": { \"Value\": 900 }",
            "\"storageCapacityM3\": { \"Value\": null }");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(withNull, Encoding.UTF8, "application/json"),
        });

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => NewHandler(handler).RecomputeAsync(Anchor));

        Assert.Contains("storageCapacityM3", thrown.Message);
        Assert.Contains("WaterReserve", thrown.Message);
        Assert.Contains("no value", thrown.Message);
    }

    [Fact]
    public async Task Reads_inputs_computes_and_writes_days_of_supply_onto_the_anchor()
    {
        var handler = new RecordingHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnchorInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var outputs = await NewHandler(handler).RecomputeAsync(Anchor);

        Assert.Equal(21.9, outputs.DaysOfSupply, 1);
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get && r.Uri.Contains($"/api/things/{Anchor}/properties"));
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/properties/daysOfSupply/facts"));
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
        var handler = new RecordingHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stringInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var outputs = await TestCulture.InAsync(TestCulture.CommaDecimal, () => NewHandler(handler).RecomputeAsync(Anchor));

        Assert.Equal(21.9, outputs.DaysOfSupply, 1);
    }

    [Fact]
    public async Task Fails_when_a_required_input_is_missing_from_the_anchor()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "population": { "Value": 300 } }""", Encoding.UTF8, "application/json"),
        });

        await Assert.ThrowsAsync<KeyNotFoundException>(() => NewHandler(handler).RecomputeAsync(Anchor));
    }
}
