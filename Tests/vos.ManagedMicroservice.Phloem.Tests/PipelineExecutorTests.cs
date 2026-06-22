using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Phloem.Configuration;
using vos.ManagedMicroservice.Phloem.Execution;
using vos.ManagedMicroservice.Phloem.Model;
using Xunit;

namespace vos.ManagedMicroservice.Phloem.Tests;

// The orchestration core (Feature #5628, #5632): synchronous spawn-and-wait, routing each node's outputs to
// its downstream inputs, halting dependents on failure. Mycelium is faked so the logic is tested without HTTP.
public class PipelineExecutorTests
{
    [Fact]
    public async Task RunAsync_HappyPath_RoutesOutputsToInputsAndSucceeds()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (subdomain, envelope) => subdomain switch
            {
                // Generate emits echo="hi"
                "gen" => NodeOk(("echo", "hi")),
                // Echo emits whatever was wired into its "message" input
                "ech" => NodeOk(("echo", InputValue(envelope, "message"))),
                _ => NodeFail("unexpected subdomain"),
            },
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Nodes.Should().OnlyContain(n => n.Status == RunStatus.Succeeded);
        // Echo's output is the value routed from Generate.echo → Echo.message
        var echo = result.Nodes.Single(n => n.Name == "Echo");
        echo.Outputs["echo"].GetString().Should().Be("hi");
        // dispatched in dependency order
        gateway.Dispatched.Should().Equal("gen", "ech");
        gateway.StatusUpdates.Should().Contain(RunStatus.Succeeded);
    }

    [Fact]
    public async Task RunAsync_NodeFailure_HaltsDependentsAndFailsRun()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (subdomain, _) => subdomain == "gen" ? NodeFail("generate boom") : NodeOk(("echo", "x")),
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Nodes.Single(n => n.Name == "Generate").Status.Should().Be(RunStatus.Failed);
        result.Nodes.Single(n => n.Name == "Echo").Status.Should().Be(RunStatus.Skipped);
        gateway.Dispatched.Should().Equal("gen"); // Echo never dispatched
        gateway.StatusUpdates.Should().Contain(RunStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_UnknownPipeline_ReturnsFailureNotThrow()
    {
        var gateway = new FakeGateway(new GraphFixture().Build()); // empty graph
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);

        var result = await executor.RunAsync(Guid.NewGuid(), default, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
        gateway.Dispatched.Should().BeEmpty();
    }

    // --- helpers ---

    private static NodeDispatchResult NodeOk(params (string Port, string Value)[] outputs)
    {
        var outs = outputs.ToDictionary(o => o.Port, o => o.Value);
        return new NodeDispatchResult(200, JsonSerializer.Serialize(new { success = true, outputs = outs, error = (string?)null }));
    }

    private static NodeDispatchResult NodeFail(string error) =>
        new(200, JsonSerializer.Serialize(new { success = false, outputs = new Dictionary<string, string>(), error }));

    private static string InputValue(JsonElement envelope, string port) =>
        envelope.GetProperty("inputs").GetProperty(port).GetString()!;

    private sealed class FakeGateway : IMyceliumGateway
    {
        private readonly PipelineGraph _graph;
        public FakeGateway(PipelineGraph graph) => _graph = graph;

        public Func<string, JsonElement, NodeDispatchResult> OnDispatch { get; set; } = (_, _) => new NodeDispatchResult(200, "{\"success\":true,\"outputs\":{}}");
        public List<string> Dispatched { get; } = new();
        public List<string> StatusUpdates { get; } = new();
        public List<NodeRunResult> NodeRuns { get; } = new();

        public Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken ct) => Task.FromResult(_graph);
        public Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken ct) => Task.CompletedTask;
        public Task PersistNodeRunAsync(Guid runId, NodeRunResult node, CancellationToken ct) { NodeRuns.Add(node); return Task.CompletedTask; }
        public Task SetRunStatusAsync(Guid runId, string status, CancellationToken ct) { StatusUpdates.Add(status); return Task.CompletedTask; }

        public Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken ct)
        {
            Dispatched.Add(subdomain);
            return Task.FromResult(OnDispatch(subdomain, envelope));
        }
    }
}
