using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.EnergyBalance.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.EnergyBalance.Tests;

// Story #5839 — the reactive (model-driven) energy analysis: read inputs off the anchor, compute, write outputs back.
public class EnergyBalanceReactiveHandlerTests
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

    // area 13500 * resource 1600 * eff 0.2 / 1000 = 4320 solar; + 100 other = 4420 total; / 4000 consumption * 100 = 110.5%.
    private const string AnchorInputs = """
        { "solarPvAreaM2": { "Value": 13500 }, "solarResourceKwhPerM2PerYear": { "Value": 1600 },
          "pvEfficiency": { "Value": 0.2 }, "otherGenerationMwhPerYear": { "Value": 100 },
          "annualConsumptionMwhPerYear": { "Value": 4000 } }
        """;

    [Fact]
    public async Task Reads_inputs_computes_and_writes_outputs_onto_the_anchor()
    {
        var handler = new RecordingHandler(req => req.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnchorInputs, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK));

        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        var outputs = await reactive.RecomputeAsync(Anchor);

        Assert.Equal(110.5, outputs.PctOfConsumption, 3);
        Assert.True(outputs.NetPositive);

        // It read the anchor and wrote pctOfConsumption back onto it.
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get && r.Uri.Contains($"/api/things/{Anchor}/properties"));
        var pctWrite = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Uri.Contains("/properties/pctOfConsumption/facts"));
        Assert.Contains("110.5", pctWrite.Body);
    }

    [Fact]
    public async Task Fails_when_a_required_input_is_missing_from_the_anchor()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "pvEfficiency": { "Value": 0.2 } }""", Encoding.UTF8, "application/json"),
        });
        var reactive = new EnergyBalanceReactiveHandler(
            new TestHttpClientFactory(new HttpClient(handler)), NullLogger<EnergyBalanceReactiveHandler>.Instance,
            "http://mycelium", serviceToken: "test-token");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => reactive.RecomputeAsync(Anchor));
    }
}
