// Contract tests for DagNodeService: envelope detection, literal- and
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

    [Theory]
    [InlineData("""{"runId":"5628da90-0000-0000-0000-0000000000aa","nodeId":"5628da90-0000-0000-0000-0000000000bb"}""", true)]
    [InlineData("""{"runId":"5628da90-0000-0000-0000-0000000000aa"}""", false)]   // nodeId missing
    [InlineData("""{"message":"hi"}""", false)]                                     // legacy /handle body
    public void IsNodeEnvelope_RequiresRunIdAndNodeId(string json, bool expected)
    {
        var root = JsonDocument.Parse(json).RootElement;
        DagNodeService.IsNodeEnvelope(root).Should().Be(expected);
    }

    [Fact]
    public async Task HandleNodeAsync_LiteralInput_RunsNodeAndReturnsOutputs()
    {
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected for literal inputs"));
        var root = Envelope(inputs: """{"message":"hello"}""");

        var response = await node.HandleNodeAsync(root);

        response.Result.Should().Be(NodeResponse.Succeeded);
        response.Error.Should().BeNull();
        response.Outputs["echo"].Should().Be("hello");
    }

    [Theory]
    [InlineData(false, "hi", """{"result":"success","outputs":{"echo":"hi"},"error":null}""")]
    [InlineData(true, "hi", """{"result":"fail","outputs":{},"error":"boom"}""")]
    public async Task An_answer_names_its_result_as_the_word_the_orchestrator_reads(bool nodeThrows, string message, string answer)
    {
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected"), alwaysThrow: nodeThrows);

        var response = await node.HandleNodeAsync(Envelope(inputs: JsonSerializer.Serialize(new { message })));

        JsonSerializer.Serialize(response).Should().Be(answer);
    }

    [Theory]
    [InlineData(30.0, """{"result":"fail","outputs":{"call":"North"},"error":"the provider is down","retry":{"afterSeconds":30}}""")]
    [InlineData(null, """{"result":"fail","outputs":{"call":"North"},"error":"the provider is down","retry":{}}""")]
    public async Task A_step_throwing_that_its_failure_can_be_tried_again_answers_so_with_the_wait_it_asks_for(double? afterSeconds, string answer)
    {
        var failure = new FailureCanBeTriedAgainException("the provider is down",
            afterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            new Dictionary<string, object?> { ["call"] = "North" });
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected"), throwing: failure);

        var response = await node.HandleNodeAsync(Envelope(inputs: """{"message":"hi"}"""));

        JsonSerializer.Serialize(response).Should().Be(answer);
    }

    [Fact]
    public async Task A_step_throwing_a_final_failure_with_outputs_answers_fail_with_them_and_no_retry()
    {
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected"),
            throwing: new NodeFailedException("400: lat out of range", new Dictionary<string, object?> { ["call"] = "North" }));

        var response = await node.HandleNodeAsync(Envelope(inputs: """{"message":"hi"}"""));

        JsonSerializer.Serialize(response).Should().Be("""{"result":"fail","outputs":{"call":"North"},"error":"400: lat out of range"}""");
    }

    [Fact]
    public void A_port_says_it_takes_a_collection_only_when_it_does()
    {
        JsonSerializer.Serialize(PortDescriptor.Input("call", "object", required: true, collection: true))
            .Should().Be("""{"portName":"call","direction":"in","type":"object","required":true,"collection":true}""");
        JsonSerializer.Serialize(PortDescriptor.Input("message", "string"))
            .Should().Be("""{"portName":"message","direction":"in","type":"string","required":false}""");
    }

    [Fact]
    public async Task A_step_throwing_that_its_failure_can_be_tried_again_with_no_outputs_answers_none()
    {
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected"),
            throwing: new FailureCanBeTriedAgainException("the provider is down"));

        var response = await node.HandleNodeAsync(Envelope(inputs: """{"message":"hi"}"""));

        JsonSerializer.Serialize(response).Should().Be("""{"result":"fail","outputs":{},"error":"the provider is down","retry":{}}""");
    }

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

        response.Result.Should().Be(NodeResponse.Succeeded);
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

        response.Result.Should().Be(NodeResponse.Failed);
        response.Error.Should().Contain("text");
        response.Outputs.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleNodeAsync_NodeThrows_ReturnsFailureNotException()
    {
        var (node, _) = BuildNode(_ => throw new InvalidOperationException("no HTTP expected"), alwaysThrow: true);
        var root = Envelope(inputs: """{"message":"hi"}""");

        var response = await node.HandleNodeAsync(root);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Error.Should().Contain("boom");
        response.Outputs.Should().BeEmpty();
    }

    [Fact]
    public void Ports_AdvertiseInputAndOutputForManifest()
    {
        var (node, _) = BuildNode(_ => Json(HttpStatusCode.OK, "{}"));

        node.Ports.Should().ContainSingle(p => p.PortName == "message" && p.Direction == "in" && p.Required);
        node.Ports.Should().ContainSingle(p => p.PortName == "echo" && p.Direction == "out" && !p.Required);
    }

    private static JsonElement Envelope(string inputs) =>
        JsonDocument.Parse($$"""{"runId":"{{Run}}","nodeId":"{{Node}}","params":{},"inputs":{{inputs}}}""").RootElement;

    // A single wire input "message" fed by a graph reference {"ref":{"thingId","property"}}.
    private static string RefInput(Guid thingId, string property) =>
        JsonSerializer.Serialize(new { message = new { @ref = new { thingId, property } } });

    private static (EchoNode node, MockHttpMessageHandler handler) BuildNode(
        Func<HttpRequestMessage, HttpResponseMessage> respond, bool alwaysThrow = false, Exception? throwing = null)
    {
        var handler = new MockHttpMessageHandler(respond);
        var http = new HttpClient(handler);
        var factory = new TestHttpClientFactory(http);
        return (new EchoNode(factory, throwing ?? (alwaysThrow ? new InvalidOperationException("boom") : null)), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // Minimal node: echoes its "message" input to an "echo" output.
    private sealed class EchoNode : DagNodeService
    {
        private readonly Exception? _failure;

        public EchoNode(IHttpClientFactory httpClientFactory, Exception? failure)
            : base(httpClientFactory, NullLogger.Instance, DagNodeServiceTests.MyceliumUrl, new LaunchToken(DagNodeServiceTests.ServiceToken)) => _failure = failure;

        public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
        {
            PortDescriptor.Input("message", "string", required: true),
            PortDescriptor.Output("echo", "string"),
        };

        protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
        {
            if (_failure is not null) throw _failure;
            var message = context.Input("message")?.GetString()
                ?? throw new InvalidOperationException("required input 'message' missing");
            return Task.FromResult(NodeResult.Ok(("echo", message)));
        }
    }
}
