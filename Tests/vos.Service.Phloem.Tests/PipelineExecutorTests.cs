using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// The orchestration core: synchronous spawn-and-wait, routing each node's outputs to
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
                "gen" => NodeOk(("echo", "hi")),
                "ech" => NodeOk(("echo", InputValue(envelope, "message"))),
                _ => NodeFail("unexpected subdomain"),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Nodes.Should().OnlyContain(n => n.Status == RunStatus.Succeeded);
        var echo = result.Nodes.Single(n => n.Name == "Echo");
        echo.Outputs["echo"].GetString().Should().Be("hi");
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
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

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
        var (fx, _) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

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
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

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
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

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
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var runParams = JsonSerializer.SerializeToElement(new { greeting = "hello" });
        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        seenMessage.Should().Be("hello"); // input `message` filled from param `greeting`
    }

    [Fact]
    public async Task RunAsync_SiteAnalysis_RoutesRunParamsIntoWaterReserveAndSurfacesDaysOfSupply()
    {
        var (fx, pipelineId) = TestGraphs.SiteAnalysisWaterPipeline();
        JsonElement seenInputs = default;
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (subdomain, envelope) =>
            {
                subdomain.Should().Be("water-reserve");
                seenInputs = envelope.GetProperty("inputs").Clone();
                return NodeOk(("daysOfSupply", "14"));
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        // 730 residents x 50 m3/yr = 36,500/yr; 1,400 m3 stored = 14 days of supply.
        var runParams = JsonSerializer.SerializeToElement(new { population = 730.0, perCapitaConsumptionM3 = 50.0, storageCapacityM3 = 1400.0 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Dispatched.Should().Equal("water-reserve");
        seenInputs.GetProperty("population").GetDouble().Should().Be(730.0);
        seenInputs.GetProperty("storageCapacityM3").GetDouble().Should().Be(1400.0);
        result.Nodes.Single(n => n.Name == "WaterReserve").Outputs["daysOfSupply"].GetString().Should().Be("14");
    }

    [Fact]
    public async Task RunAsync_EnergyAnalysis_RoutesRunParamsIntoEnergyBalanceAndSurfacesNetPositive()
    {
        var (fx, pipelineId) = TestGraphs.SiteAnalysisEnergyPipeline();
        JsonElement seenInputs = default;
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (subdomain, envelope) =>
            {
                subdomain.Should().Be("energy-balance");
                seenInputs = envelope.GetProperty("inputs").Clone();
                return NodeOk(("percentOfConsumption", "112"), ("netPositive", "true"));
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var runParams = JsonSerializer.SerializeToElement(new
        {
            solarPvAreaM2 = 29611.0,
            solarResourceKwhPerM2PerYear = 2279.5,
            moduleEfficiency = 0.20,
            performanceRatio = 1.0,
            otherGenerationMwhPerYear = 7400.0,
            annualConsumptionMwhPerYear = 18743.0,
        });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Dispatched.Should().Equal("energy-balance");
        seenInputs.GetProperty("solarPvAreaM2").GetDouble().Should().Be(29611.0);
        seenInputs.GetProperty("annualConsumptionMwhPerYear").GetDouble().Should().Be(18743.0);
        result.Nodes.Single(n => n.Name == "EnergyBalance").Outputs["netPositive"].GetString().Should().Be("true");
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
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
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
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
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
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" }, w = 1 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue(); // a partial fan-out does not fail the run
        var node = result.Nodes.Single();
        node.Status.Should().Be(RunStatus.Partial);
        node.Outputs["score"].EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Null ? null : e.GetString())
            .Should().Equal("A", null, "C"); // failed item is a null hole, in order
    }

    [Fact] // Two wires into one input deep-merge by their to-paths
    public async Task RunAsync_TwoWiresIntoOneInput_DeepMergeByToPath()
    {
        var (fx, pipelineId) = TestGraphs.FieldMergePipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (subdomain, _) => subdomain switch
            {
                "a" => NodeOk(("out", "AA")),
                "b" => NodeOk(("out", "BB")),
                "c" => NodeOk(("out", "done")),
                _ => NodeFail("unexpected subdomain"),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        // C's single `in` input carries both upstream outputs, merged under their to-paths — neither overwrote.
        var envelope = gateway.Envelopes.Single(e => e.Subdomain == "c").Envelope;
        var input = envelope.GetProperty("inputs").GetProperty("in");
        input.GetProperty("a").GetString().Should().Be("AA");
        input.GetProperty("b").GetString().Should().Be("BB");
    }

    [Fact] // A JSONata transform reshapes the upstream output before the downstream input
    public async Task RunAsync_WireTransform_ReshapesUpstreamOutput()
    {
        var (fx, pipelineId) = TestGraphs.WireTransformPipeline("{\"name\": firstName & \" \" & lastName}");
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (subdomain, _) => subdomain switch
            {
                "a" => new NodeDispatchResult(200, JsonSerializer.Serialize(new
                {
                    success = true,
                    outputs = new Dictionary<string, object> { ["out"] = new { firstName = "Ada", lastName = "Lovelace" } },
                    error = (string?)null,
                })),
                "c" => NodeOk(("out", "done")),
                _ => NodeFail("unexpected subdomain"),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        var envelope = gateway.Envelopes.Single(e => e.Subdomain == "c").Envelope;
        envelope.GetProperty("inputs").GetProperty("in").GetProperty("name").GetString().Should().Be("Ada Lovelace");
    }

    [Fact] // An invalid transform is caught at pre-run validation — the run fails without dispatching
    public async Task RunAsync_InvalidWireTransform_FailsValidationBeforeDispatch()
    {
        var (fx, pipelineId) = TestGraphs.WireTransformPipeline("this is ( not valid jsonata");
        var gateway = new FakeGateway(fx.Build()) { OnDispatch = (_, _) => NodeOk(("out", "x")) };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("invalid").And.Contain("transform");
        gateway.Dispatched.Should().BeEmpty("a pipeline that fails validation never dispatches a node");
    }

    [Fact] // Input node output ports are filled from run params
    public async Task RunAsync_InputBoundaryNode_SeedsOutputPortsFromRunParams()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var runParams = JsonSerializer.SerializeToElement(new { seed = "hi" });
        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Nodes.Single(n => n.Name == "Echo").Outputs["echo"].GetString().Should().Be("hi");
        gateway.Dispatched.Should().Equal("ech");
    }

    [Fact] // A value wired into the Output node becomes the run result
    public async Task RunAsync_OutputBoundaryNode_CollectsWiredInputAsRunResult()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnDispatch = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var runParams = JsonSerializer.SerializeToElement(new { seed = "world" });
        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Result.Should().NotBeNull();
        result.Result!.Value.GetProperty("result").GetString().Should().Be("world");
        gateway.RunResult.Should().NotBeNull();
        gateway.RunResult!.Value.GetProperty("result").GetString().Should().Be("world");
    }

    [Fact]
    public async Task RunAsync_EndingAtAnExternalSystem_SendsWhatReachedTheEndAlongTheSystemsConnection()
    {
        var telling = TestGraphs.TellingPipeline();
        var gateway = new FakeGateway(telling.Fixture.Build())
        {
            OnDispatch = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(telling.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        result.Success.Should().BeTrue();
        var sent = gateway.MessagesSent.Should().ContainSingle().Subject;
        sent.RunId.Should().Be(result.RunId);
        sent.Telling.Should().Be(new Telling(telling.SystemId, "Reporting office", telling.ConnectionId));
        sent.Payload.GetProperty("result").GetString().Should().Be("world");
        result.Result!.Value.GetProperty("result").GetString().Should().Be("world", "the run still publishes what reached its end");
    }

    [Fact]
    public async Task RunAsync_WhenTheMessageCannotBeWritten_FailsTheEndNodeNamingTheSystem()
    {
        var telling = TestGraphs.TellingPipeline();
        var gateway = new FakeGateway(telling.Fixture.Build())
        {
            OnDispatch = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
            OnSend = () => throw new HttpRequestException("the broker refused the write"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(telling.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        result.Success.Should().BeFalse();
        var end = result.Nodes.Single(node => node.Name == "Out");
        end.Status.Should().Be(RunStatus.Failed);
        end.Error.Should().Contain("Reporting office").And.Contain("the broker refused the write");
    }

    [Fact]
    public async Task RunAsync_EndingAtASystemThatNamesNoConnection_FailsBeforeAnyNodeRuns()
    {
        var telling = TestGraphs.TellingPipeline(namesItsConnection: false);
        var gateway = new FakeGateway(telling.Fixture.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(telling.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Dispatched.Should().BeEmpty();
        gateway.MessagesSent.Should().BeEmpty();
        gateway.RunError.Should().Contain("Reporting office");
    }

    // A Thing entering a watched state is the run's subject: it is recorded on the run and reaches the
    // start node as the `subject` param — the name the page gives the port — carrying the Thing's id and
    // name, so a wire narrows it to either and the pipeline hands it down like any other.
    [Fact]
    public async Task RunAsync_WithASubject_RecordsItOnTheRunAndSeedsTheStartNodeWithIt()
    {
        var fixture = TestGraphs.StatePipeline();
        var gateway = new FakeGateway(fixture.Fixture.Build())
        {
            OnDispatch = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var subject = new RunSubject(Guid.NewGuid(), "Submission 42");

        var result = await executor.RunAsync(fixture.DrawnPipelineId, default, CancellationToken.None, subject: subject);

        result.Success.Should().BeTrue();
        result.Result!.Value.GetProperty("result").GetString().Should().Be(subject.Id.ToString());
        gateway.RunsCreated.Single().Subject.Should().Be(subject);
    }

    // The subject is the broker's word on what entered, so a relationship property spelled the same way
    // does not displace it.
    [Fact]
    public async Task RunAsync_TheSubjectWinsOverAParamOfTheSameName()
    {
        var fixture = TestGraphs.StatePipeline();
        var gateway = new FakeGateway(fixture.Fixture.Build())
        {
            OnDispatch = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var subject = new RunSubject(Guid.NewGuid(), "Submission 42");
        var runParams = JsonSerializer.SerializeToElement(new { subject = new { id = "somebody else" }, kept = "yes" });

        var result = await executor.RunAsync(fixture.DrawnPipelineId, runParams, CancellationToken.None, subject: subject);

        result.Result!.Value.GetProperty("result").GetString().Should().Be(subject.Id.ToString());
    }

    // A run refused before any node dispatches has no node run to carry a reason, and nobody awaits a
    // run a state entry started; the reason lives on the run.
    [Fact]
    public async Task RunAsync_ARunRefusedBeforeAnyNode_LeavesItsReasonOnTheRun()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        fx.Rel(fx.Get("Echo"), fx.Get("carries"), fx.Get("Generate"), ("fromPort", "echo"), ("toPort", "message"));
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.RunError.Should().Be(result.Error).And.Contain("invalid");
    }

    [Fact]
    public async Task RunAsync_WithoutASubject_RecordsNone()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        await executor.RunAsync(pipelineId, default, CancellationToken.None);

        gateway.RunsCreated.Single().Subject.Should().BeNull();
        gateway.RunsCreated.Single().RequestId.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WithTheRequestThatStartedIt_RecordsThatRequestsIdentifierOnTheRun()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var requestId = Guid.NewGuid();

        await executor.RunAsync(pipelineId, default, CancellationToken.None, requestId: requestId);

        gateway.RunsCreated.Single().RequestId.Should().Be(requestId);
    }

    private static NodeDispatchResult NodeOk(params (string Port, string Value)[] outputs)
    {
        var outs = outputs.ToDictionary(o => o.Port, o => o.Value);
        return new NodeDispatchResult(200, JsonSerializer.Serialize(new { success = true, outputs = outs, error = (string?)null }));
    }

    private static NodeDispatchResult NodeFail(string error) =>
        new(200, JsonSerializer.Serialize(new { success = false, outputs = new Dictionary<string, string>(), error }));

    private static string InputValue(JsonElement envelope, string port) =>
        envelope.GetProperty("inputs").GetProperty(port).GetString()!;
}
