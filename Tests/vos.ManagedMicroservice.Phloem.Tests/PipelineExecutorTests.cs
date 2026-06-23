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

    [Fact]
    public async Task RunAsync_CancelRequested_HaltsRemainingNodesAndCancelsRun()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        FakeGateway gateway = null!;
        gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (_, _) => NodeOk(("echo", "x")),
            // Cancel becomes true once the first node has run — checked before the next level dispatches.
            CancelRequested = _ => gateway.Dispatched.Contains("gen"),
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("cancelled");
        result.Nodes.Single(n => n.Name == "Generate").Status.Should().Be(RunStatus.Succeeded);
        result.Nodes.Single(n => n.Name == "Echo").Status.Should().Be(RunStatus.Cancelled);
        gateway.Dispatched.Should().Equal("gen"); // Echo never dispatched
        gateway.StatusUpdates.Should().Contain(RunStatus.Cancelled);
    }

    [Fact]
    public async Task RunAsync_PersistsRunningThenTerminalForEachNode()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (subdomain, envelope) => subdomain switch
            {
                "gen" => NodeOk(("echo", "hi")),
                "ech" => NodeOk(("echo", InputValue(envelope, "message"))),
                _ => NodeFail("unexpected subdomain"),
            },
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);

        await executor.RunAsync(pipelineId, default, CancellationToken.None);

        // Each node is persisted running first (live animation), then its terminal status — on one NodeRun.
        gateway.NodeStatuses.Where(s => s.Name == "Generate").Select(s => s.Status)
            .Should().Equal(RunStatus.Running, RunStatus.Succeeded);
        gateway.NodeStatuses.Where(s => s.Name == "Echo").Select(s => s.Status)
            .Should().Equal(RunStatus.Running, RunStatus.Succeeded);
    }

    [Fact]
    public async Task RunAsync_BindsRunParamToBoundNodeInput()
    {
        var (fx, pipelineId) = TestGraphs.ParamBoundPipeline();
        string? seenMessage = null;
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (_, envelope) =>
            {
                seenMessage = envelope.GetProperty("inputs").TryGetProperty("message", out var m) ? m.GetString() : null;
                return NodeOk(("echo", seenMessage ?? ""));
            },
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);

        var runParams = JsonSerializer.SerializeToElement(new { greeting = "hello" });
        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        seenMessage.Should().Be("hello"); // input `message` filled from param `greeting`
    }

    [Fact]
    public async Task RunAsync_FanOut_RunsPerItem_BroadcastsScalar_GathersOutputs()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline();
        var items = new List<string>();
        var weights = new List<int>();
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (_, envelope) =>
            {
                var inputs = envelope.GetProperty("inputs");
                var item = inputs.GetProperty("item").GetString()!;
                lock (items) { items.Add(item); weights.Add(inputs.GetProperty("weight").GetInt32()); }
                return NodeOk(("score", item.ToUpperInvariant()));
            },
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" }, w = 10 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        items.Should().BeEquivalentTo("a", "b", "c");      // ran once per item
        weights.Should().AllBeEquivalentTo(10);            // scalar input broadcast to every item
        var node = result.Nodes.Single();
        node.Status.Should().Be(RunStatus.Succeeded);
        node.Outputs["score"].EnumerateArray().Select(e => e.GetString()).Should().Equal("A", "B", "C"); // gathered, in order
    }

    [Fact]
    public async Task RunAsync_FanOut_FailFast_FailsNodeWhenAnyItemFails()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline("fail");
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (_, envelope) =>
                envelope.GetProperty("inputs").GetProperty("item").GetString() == "b" ? NodeFail("boom") : NodeOk(("score", "ok")),
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" }, w = 1 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Nodes.Single().Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_FanOut_Continue_IsPartialWithNullHoles()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline("continue");
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (_, envelope) =>
            {
                var item = envelope.GetProperty("inputs").GetProperty("item").GetString()!;
                return item == "b" ? NodeFail("boom") : NodeOk(("score", item.ToUpperInvariant()));
            },
        };
        var executor = new PipelineExecutor(gateway, new PipelineModelOptions(), NullLogger<PipelineExecutor>.Instance);
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" }, w = 1 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue(); // a partial fan-out does not fail the run
        var node = result.Nodes.Single();
        node.Status.Should().Be(RunStatus.Partial);
        node.Outputs["score"].EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Null ? null : e.GetString())
            .Should().Equal("A", null, "C"); // failed item is a null hole, in order
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
        public Func<Guid, bool> CancelRequested { get; set; } = _ => false;
        public List<string> Dispatched { get; } = new();
        public List<string> StatusUpdates { get; } = new();
        public List<(string Name, string Status)> NodeStatuses { get; } = new();

        public Task<PipelineGraph> LoadPipelineSubgraphAsync(Guid pipelineId, CancellationToken ct) => Task.FromResult(_graph);
        public Task CreateRunAsync(Guid runId, Guid pipelineId, CancellationToken ct) => Task.CompletedTask;
        public Task SetNodeRunStatusAsync(Guid runId, Guid nodeId, string nodeName, string status, string? error, CancellationToken ct, int? index = null, int total = 0)
        {
            lock (NodeStatuses) NodeStatuses.Add((nodeName, status));
            return Task.CompletedTask;
        }
        public Task SetRunStatusAsync(Guid runId, string status, CancellationToken ct) { StatusUpdates.Add(status); return Task.CompletedTask; }
        public Task<bool> IsCancelRequestedAsync(Guid runId, CancellationToken ct) => Task.FromResult(CancelRequested(runId));

        public Task<NodeDispatchResult> DispatchAsync(string subdomain, JsonElement envelope, CancellationToken ct)
        {
            lock (Dispatched) Dispatched.Add(subdomain);
            return Task.FromResult(OnDispatch(subdomain, envelope));
        }
    }
}
