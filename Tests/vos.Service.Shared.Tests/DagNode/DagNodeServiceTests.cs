// Contract tests for DagNodeService (Feature #5628, Phase #5630): envelope detection, literal- and
// reference-input resolution, the failure-not-500 contract, and the advertised port manifest.

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared.DagNode;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.DagNode;

public class DagNodeServiceTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string ServiceToken = "service-token-abc";
    private static readonly Guid Run = Guid.Parse("5628da90-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("5628da90-0000-0000-0000-0000000000bb");
    private static readonly Guid Source = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---------------- Envelope detection ----------------

    [Theory]
    [InlineData("""{"runId":"5628da90-0000-0000-0000-0000000000aa","nodeId":"5628da90-0000-0000-0000-0000000000bb"}""", true)]
    [InlineData("""{"runId":"5628da90-0000-0000-0000-0000000000aa"}""", false)]   // nodeId missing
    [InlineData("""{"message":"hi"}""", false)]                                     // legacy /handle body
    public void IsNodeEnvelope_RequiresRunIdAndNodeId(string json, bool expected)
    {
        var root = JsonDocument.Parse(json).RootElement;
        DagNodeService.IsNodeEnvelope(root).Should().Be(expected);
    }

    // ---------------- Literal inputs ----------------

    [Fact]
    public async Task HandleNodeAsync_LiteralInput_RunsNodeAndReturnsOutputs()
    {
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected for literal inputs"));
        var root = Envelope(inputs: """{"message":"hello"}""");

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeTrue();
        response.Error.Should().BeNull();
        response.Outputs["echo"].Should().Be("hello");
    }

    // ---------------- Reference inputs ----------------

    [Fact]
    public async Task HandleNodeAsync_RefInput_ResolvedFromEffectiveProperties()
    {
        HttpRequestMessage? captured = null;
        var (node, _) = BuildNode(req =>
        {
            captured = req;
            return Json(HttpStatusCode.OK, """{"text":{"Value":"resolved-from-graph"}}""");
        });
        var root = Envelope(RefInput(Source, "text"));

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeTrue();
        response.Outputs["echo"].Should().Be("resolved-from-graph");
        captured!.Method.Should().Be(HttpMethod.Get);
        captured.RequestUri!.AbsoluteUri.Should().Be($"{MyceliumUrl}/api/things/{Source}/properties");
    }

    [Fact]
    public async Task HandleNodeAsync_RefInput_PropertyMissing_ReportsFailure()
    {
        var (node, _) = BuildNode(_ => Json(HttpStatusCode.OK, """{"other":{"Value":1}}"""));
        var root = Envelope(RefInput(Source, "text"));

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("text");
        response.Outputs.Should().BeEmpty();
    }

    // ---------------- Failure contract ----------------

    [Fact]
    public async Task HandleNodeAsync_NodeThrows_ReturnsFailureNotException()
    {
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected"), alwaysThrow: true);
        var root = Envelope(inputs: """{"message":"hi"}""");

        var response = await node.HandleNodeAsync(root);

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("boom");
        response.Outputs.Should().BeEmpty();
    }

    // ---------------- Manifest ----------------

    [Fact]
    public void Ports_AdvertiseInputAndOutputForManifest()
    {
        var (node, _) = BuildNode(_ => Json(HttpStatusCode.OK, "{}"));

        node.Ports.Should().ContainSingle(p => p.PortName == "message" && p.Direction == "in" && p.Required);
        node.Ports.Should().ContainSingle(p => p.PortName == "echo" && p.Direction == "out" && !p.Required);
    }

    // ---------------- Helpers ----------------

    private static JsonElement Envelope(string inputs) =>
        JsonDocument.Parse($$"""{"runId":"{{Run}}","nodeId":"{{Node}}","params":{},"inputs":{{inputs}}}""").RootElement;

    // A single wire input "message" fed by a graph reference {"ref":{"thingId","property"}}.
    private static string RefInput(Guid thingId, string property) =>
        JsonSerializer.Serialize(new { message = new { @ref = new { thingId, property } } });

    private static (EchoNode node, MockHttpMessageHandler handler) BuildNode(
        Func<HttpRequestMessage, HttpResponseMessage> respond, bool alwaysThrow = false)
    {
        var handler = new MockHttpMessageHandler(respond);
        var http = new HttpClient(handler);
        var factory = new TestHttpClientFactory(http);
        return (new EchoNode(factory, alwaysThrow), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // Minimal node: echoes its "message" input to an "echo" output (the #5631 reference shape).
    private sealed class EchoNode : DagNodeService
    {
        private readonly bool _throw;

        public EchoNode(IHttpClientFactory httpClientFactory, bool @throw)
            : base(httpClientFactory, NullLogger.Instance, DagNodeServiceTests.MyceliumUrl, DagNodeServiceTests.ServiceToken) => _throw = @throw;

        public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
        {
            PortDescriptor.Input("message", "string", required: true),
            PortDescriptor.Output("echo", "string"),
        };

        protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
        {
            if (_throw) throw new InvalidOperationException("boom");
            var message = context.Input("message")?.GetString()
                ?? throw new InvalidOperationException("required input 'message' missing");
            return Task.FromResult(NodeResult.Ok(("echo", message)));
        }
    }
}
