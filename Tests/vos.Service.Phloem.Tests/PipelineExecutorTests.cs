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
            OnCall = (subdomain, envelope) => subdomain switch
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
        gateway.Called.Should().Equal("gen", "ech");
        gateway.StatusUpdates.Should().Contain(RunStatus.Succeeded);
    }

    [Fact]
    public async Task RunAsync_NodeFailure_HaltsDependentsAndFailsRun()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (subdomain, _) => subdomain == "gen" ? NodeFail("generate boom") : NodeOk(("echo", "x")),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Nodes.Single(n => n.Name == "Generate").Status.Should().Be(RunStatus.Failed);
        result.Nodes.Single(n => n.Name == "Echo").Status.Should().Be(RunStatus.Skipped);
        gateway.Called.Should().Equal("gen"); // Echo never called
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
        gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_StoppedWhileANodeIsCalled_StopsAtTheNextNodeRecordingTheReason()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var request = Guid.NewGuid();
        PipelineExecutor executor = null!;
        Task? stopping = null;
        var gateway = new FakeGateway(fx.Build());
        gateway.OnCall = (_, envelope) =>
        {
            stopping ??= executor.StopAsync(request, envelope.GetProperty("runId").GetGuid(), CancellationToken.None);
            return NodeOk(("echo", "x"));
        };
        executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);
        await stopping!;

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("withdrawn by its owner");
        result.Nodes.Single(n => n.Name == "Generate").Status.Should().Be(RunStatus.Succeeded);
        var echo = result.Nodes.Single(n => n.Name == "Echo");
        echo.Status.Should().Be(RunStatus.Cancelled);
        echo.Error.Should().Be("withdrawn by its owner");
        gateway.Called.Should().Equal("gen");
        gateway.RunStatuses[result.RunId].Should().Be(RunStatus.Cancelled);
        gateway.RunError.Should().Be("withdrawn by its owner");
        gateway.StoppedBy.Should().Equal((result.RunId, request));
    }

    [Fact]
    public async Task RunAsync_PersistsRunningThenTerminalForEachNode()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (subdomain, envelope) => subdomain switch
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
            OnCall = (_, envelope) =>
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
            OnCall = (subdomain, envelope) =>
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
        gateway.Called.Should().Equal("water-reserve");
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
            OnCall = (subdomain, envelope) =>
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
        gateway.Called.Should().Equal("energy-balance");
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
            OnCall = (_, envelope) =>
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
        var node = result.Nodes.Single(n => n.Name == "Scorer");
        node.Status.Should().Be(RunStatus.Succeeded);
        node.Outputs["score"].EnumerateArray().Select(e => e.GetString()).Should().Equal("A", "B", "C"); // gathered, in order
    }

    [Fact]
    public async Task RunAsync_FanOut_FailFast_FailsNodeWhenAnyItemFails()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline("fail");
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (_, envelope) =>
                envelope.GetProperty("inputs").GetProperty("item").GetString() == "b" ? NodeFail("boom") : NodeOk(("score", "ok")),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" }, w = 1 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Nodes.Single(n => n.Name == "Scorer").Status.Should().Be(RunStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_FanOut_Continue_IsPartialWithNullHoles()
    {
        var (fx, pipelineId) = TestGraphs.FanOutPipeline("continue");
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (_, envelope) =>
            {
                var item = envelope.GetProperty("inputs").GetProperty("item").GetString()!;
                return item == "b" ? NodeFail("boom") : NodeOk(("score", item.ToUpperInvariant()));
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c" }, w = 1 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue(); // a partial fan-out does not fail the run
        var node = result.Nodes.Single(n => n.Name == "Scorer");
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
            OnCall = (subdomain, _) => subdomain switch
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
            OnCall = (subdomain, _) => subdomain switch
            {
                "a" => new NodeCallResult(200, JsonSerializer.Serialize(new
                {
                    result = "success",
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

    [Fact] // An invalid transform is caught at pre-run validation — the run fails without calling anything
    public async Task RunAsync_InvalidWireTransform_FailsValidationBeforeAnyCall()
    {
        var (fx, pipelineId) = TestGraphs.WireTransformPipeline("this is ( not valid jsonata");
        var gateway = new FakeGateway(fx.Build()) { OnCall = (_, _) => NodeOk(("out", "x")) };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("invalid").And.Contain("transform");
        gateway.Called.Should().BeEmpty("a pipeline that fails validation never calls a node");
    }

    [Fact] // Input node output ports are filled from run params
    public async Task RunAsync_InputBoundaryNode_SeedsOutputPortsFromRunParams()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var runParams = JsonSerializer.SerializeToElement(new { seed = "hi" });
        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Nodes.Single(n => n.Name == "Echo").Outputs["echo"].GetString().Should().Be("hi");
        gateway.Called.Should().Equal("ech");
    }

    [Fact] // A value wired into the Output node becomes the run result
    public async Task RunAsync_OutputBoundaryNode_CollectsWiredInputAsRunResult()
    {
        var (fx, pipelineId) = TestGraphs.BoundaryPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
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
    public async Task RunAsync_ReachingATellNode_SendsWhatReachedItAlongTheSystemsConnection_AndCarriesOnToTheOutputNode()
    {
        var telling = TestGraphs.TellingPipeline();
        var gateway = new FakeGateway(telling.Fixture.Build())
        {
            OnCall = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(telling.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        result.Success.Should().BeTrue();
        var sent = gateway.MessagesSent.Should().ContainSingle().Subject;
        sent.RunId.Should().Be(result.RunId);
        sent.Telling.Should().Be(new Telling(telling.SystemId, "Reporting office", telling.ConnectionId));
        sent.Payload.GetProperty("result").GetString().Should().Be("world");
        result.Nodes.Single(node => node.Name == TestGraphs.TellNode).Status.Should().Be(RunStatus.Succeeded);
        gateway.Called.Should().Equal(["ech"], "a tell node calls no service");
        result.Result!.Value.GetProperty("result").GetString().Should().Be("world", "the tell node passes on what it sent");
    }

    [Fact]
    public async Task RunAsync_WhenTheMessageCannotBeWritten_FailsTheTellNodeNamingTheSystem_AndItsFailWireCarriesTheError()
    {
        var telling = TestGraphs.TellingPipeline();
        var fx = telling.Fixture;
        var heard = fx.Thing("Out.heard", ("direction", "in"), ("type", "string"), ("portName", "heard"));
        fx.Rel(heard, fx.Get("is"), fx.Get("Port"));
        fx.Rel(fx.Get("Out"), fx.Get("has"), heard);
        var failWire = fx.WireBetween(fx.Get(TestGraphs.TellNode), fx.Get("error"), fx.Get("Out"), heard);
        fx.TakeOn(failWire, fx.Get("fail"));
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
            OnSend = () => throw new HttpRequestException("the broker refused the write"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(telling.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        result.Success.Should().BeFalse();
        var tell = result.Nodes.Single(node => node.Name == TestGraphs.TellNode);
        tell.Status.Should().Be(RunStatus.Failed);
        tell.Error.Should().Contain("Reporting office").And.Contain("the broker refused the write");
        result.Nodes.Single(node => node.Name == "Out").Status.Should().Be(RunStatus.Succeeded);
        result.Error.Should().Be("A fail path reached 'Out'.");
    }

    [Fact]
    public async Task RunAsync_ATellNodeStandingForASystemThatNamesNoConnection_FailsBeforeAnyNodeRuns()
    {
        var telling = TestGraphs.TellingPipeline(namesItsConnection: false);
        var gateway = new FakeGateway(telling.Fixture.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(telling.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Called.Should().BeEmpty();
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
            OnCall = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
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
            OnCall = (sub, env) => sub == "ech" ? NodeOk(("echo", InputValue(env, "message"))) : NodeFail("unexpected subdomain"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var subject = new RunSubject(Guid.NewGuid(), "Submission 42");
        var runParams = JsonSerializer.SerializeToElement(new { subject = new { id = "somebody else" }, kept = "yes" });

        var result = await executor.RunAsync(fixture.DrawnPipelineId, runParams, CancellationToken.None, subject: subject);

        result.Result!.Value.GetProperty("result").GetString().Should().Be(subject.Id.ToString());
    }

    // A run refused before any node is called has no node run to carry a reason, and nobody awaits a
    // run a state entry started; the reason lives on the run.
    [Fact]
    public async Task RunAsync_ARunRefusedBeforeAnyNode_LeavesItsReasonOnTheRun()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipelineInACycle();
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.RunError.Should().Be(result.Error).And.Contain("invalid");
        gateway.StatusUpdates.Should().Equal(RunStatus.Refused);
        gateway.NodeRunsPersisted.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_TwoRequiredInputsNothingFillsOnDifferentNodes_IsRefusedNamingBothAndCallsNothing()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        foreach (var prototype in new[] { "SourceProto", "EchoProto" })
        {
            var port = fx.Thing($"{prototype}.context", ("direction", "in"), ("type", "string"), ("portName", "context"), ("required", "true"));
            fx.Rel(port, fx.Get("is"), fx.Get("Port"));
            fx.Rel(fx.Get(prototype), fx.Get("has"), port);
        }
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Called.Should().BeEmpty();
        gateway.StatusUpdates.Should().Equal(RunStatus.Refused);
        gateway.RunError.Should().Contain("'Generate' needs its port 'context'").And.Contain("'Echo' needs its port 'context'");
    }

    [Fact]
    public async Task RunAsync_StartedWithoutAParameterARequiredPortIsBoundTo_IsRefusedNamingIt()
    {
        var (fx, pipelineId) = TestGraphs.ParamBoundPipeline();
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None, subject: new RunSubject(Guid.NewGuid(), "Site 3"));

        result.Success.Should().BeFalse();
        gateway.Called.Should().BeEmpty();
        gateway.RunError.Should().Contain("run parameter 'greeting'");
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

    private static FakeGateway Deciding(GraphFixture fx, object? verdict, string? decision = null) => new(fx.Build())
    {
        OnCall = (subdomain, envelope) => subdomain switch
        {
            "decide" => new NodeCallResult(200, JsonSerializer.Serialize(new
            {
                result = "success",
                decision,
                outputs = new Dictionary<string, object?> { ["out"] = "yes: " + InputValue(envelope, "in"), ["verdict"] = verdict },
            })),
            "notify" => NodeOk(("out", "told " + InputValue(envelope, "in"))),
            "return" => NodeOk(("out", "returned")),
            _ => NodeFail("unexpected subdomain"),
        },
    };

    private static Task<PipelineRunResult> DecideAsync(FakeGateway gateway, Guid pipelineId) =>
        new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock())
            .RunAsync(pipelineId, JsonSerializer.SerializeToElement(new { subject = "plot 7" }), CancellationToken.None);

    [Fact]
    public async Task RunAsync_Decision_TheChoiceWhoseRuleHolds_RunsItsBranch_RecordsTheOtherNotTaken_AndJoinsOnce()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();
        var gateway = Deciding(fx, verdict: fx.Get("approved").Id);

        var result = await DecideAsync(gateway, pipelineId);

        result.Success.Should().BeTrue();
        gateway.Called.Should().Equal("decide", "notify");
        result.Nodes.Single(n => n.Name == "Decide").Decision.Should().Be(fx.Get("accept").Id);
        result.Nodes.Single(n => n.Name == "Return").Status.Should().Be(RunStatus.NotTaken);
        gateway.NodeStatuses.Where(s => s.Name == "Return").Select(s => s.Status).Should().Equal(RunStatus.NotTaken);
        result.Nodes.Single(n => n.Name == "End").Status.Should().Be(RunStatus.Succeeded);
        result.Result!.Value.GetProperty("result").GetString().Should().Be("told yes: plot 7");
    }

    [Fact]
    public async Task RunAsync_Decision_NoRuleHolds_TakesTheChoiceWithoutARule()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();
        var gateway = Deciding(fx, verdict: "unheard of");

        var result = await DecideAsync(gateway, pipelineId);

        gateway.Called.Should().Equal("decide", "return");
        result.Nodes.Single(n => n.Name == "Decide").Decision.Should().Be(fx.Get("refuse").Id);
    }

    [Fact]
    public async Task RunAsync_Decision_ARuleReadsTheStepsInputs()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline(acceptWhen: "inputs.in = 'plot 7'");

        var gateway = Deciding(fx, verdict: null);
        await DecideAsync(gateway, pipelineId);

        gateway.Called.Should().Equal("decide", "notify");
    }

    [Fact]
    public async Task RunAsync_Decision_ADecisionInTheServicesAnswer_ChoosesNothing()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();
        var gateway = Deciding(fx, verdict: fx.Get("approved").Id, decision: fx.Get("refuse").Id.ToString());

        await DecideAsync(gateway, pipelineId);

        gateway.Called.Should().Equal("decide", "notify");
    }

    [Fact]
    public async Task RunAsync_Decision_TwoRulesHolding_FailsTheNodeNamingBoth()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline(refuseWhen: "true");

        var result = await DecideAsync(Deciding(fx, verdict: fx.Get("approved").Id), pipelineId);

        result.Nodes.Single(n => n.Name == "Decide").Error.Should().Be("'Decide' has more than one choice whose rule holds: 'accept', 'refuse'.");
        result.Error.Should().Be("'Decide' failed, and no fail wire leaves it.");
    }

    [Fact]
    public async Task RunAsync_Decision_NoRuleHoldingAndNoChoiceWithoutARule_FailsTheNode()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline(refuseWhen: "false");

        var gateway = Deciding(fx, verdict: "unheard of");
        var result = await DecideAsync(gateway, pipelineId);

        result.Nodes.Single(n => n.Name == "Decide").Error.Should().Be(
            "'Decide' succeeded, but no rule of its choices holds and it has no choice without a rule.");
        gateway.Called.Should().Equal("decide");
    }

    [Fact]
    public async Task RunAsync_Decision_ARuleAnsweringNeitherTrueNorFalse_FailsTheNodeNamingTheChoice()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline(acceptWhen: "outputs.out");

        var result = await DecideAsync(Deciding(fx, verdict: null), pipelineId);

        result.Nodes.Single(n => n.Name == "Decide").Error.Should().Be(
            "'Decide' cannot judge its choice 'accept': its rule answered \"yes: plot 7\", and a rule answers true or false.");
    }

    [Fact]
    public async Task RunAsync_Decision_ARuleThatFailsWhileJudging_FailsTheNodeNamingTheChoice()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline(acceptWhen: "$error('no verdict to read')");

        var result = await DecideAsync(Deciding(fx, verdict: null), pipelineId);

        result.Nodes.Single(n => n.Name == "Decide").Error.Should()
            .StartWith("'Decide' cannot judge its choice 'accept': ").And.Contain("no verdict to read");
    }

    [Fact]
    public async Task RunAsync_Decision_AFailAnswer_JudgesNoRule()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline(acceptWhen: "$error('judged a fail')");
        var gateway = new FakeGateway(fx.Build()) { OnCall = (_, _) => NodeFail("the plot is dry") };

        var result = await DecideAsync(gateway, pipelineId);

        result.Nodes.Single(n => n.Name == "Decide").Error.Should().Be("the plot is dry");
    }

    [Fact]
    public void Validate_AChoiceNoWireIsTakenOn_IsRefusedByName()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();
        fx.ChoiceOf(fx.Get("Decide"), "defer", "false");

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().Contain(
            "'Decide' has the choice 'defer', but no wire leaving it is taken on that choice.");
    }

    [Fact]
    public void Validate_TwoChoicesWithoutARule_AreRefused()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline();
        fx.TakeOn(fx.Wire(fx.Get("Decide"), "out", fx.Get("Return"), "in"), fx.ChoiceOf(fx.Get("Decide"), "defer"));

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().Equal(
            "'Decide' has 2 choices without a rule ('refuse', 'defer'); only one can be taken when no rule holds.");
    }

    [Fact]
    public void Validate_ARuleThatDoesNotCompile_IsRefusedNamingTheChoice()
    {
        var (fx, pipelineId) = TestGraphs.DecisionPipeline(acceptWhen: "outputs.verdict = (");

        DagValidator.Validate(PipelineDagBuilder.Build(fx.Build(), pipelineId)).Should().ContainSingle()
            .Which.Should().StartWith("'Decide' has the choice 'accept', whose rule does not compile: ");
    }

    [Fact]
    public async Task RunAsync_AThingChosenOnTheNode_FillsItsPortsInput()
    {
        var choice = TestGraphs.ChoicePipeline();
        choice.Fixture.Rel(choice.Node, choice.Offers, choice.Offered[1]);
        var gateway = new FakeGateway(choice.Fixture.Build()) { OnCall = (_, _) => NodeOk(("out", "chosen")) };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(choice.PipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        var strategy = gateway.Envelopes.Single().Envelope.GetProperty("inputs").GetProperty("strategy");
        strategy.GetProperty("id").GetString().Should().Be(choice.Offered[1].Id.ToString());
        strategy.GetProperty("name").GetString().Should().Be("Cheapest first");
    }

    [Fact]
    public async Task RunAsync_AParameterBoundToThePort_WinsOverTheThingChosen()
    {
        var choice = TestGraphs.ChoicePipeline(nodeProperties: ("parameterBindings", "{\"strategy\":\"strategy\"}"));
        choice.Fixture.Rel(choice.Node, choice.Offers, choice.Offered[1]);
        var gateway = new FakeGateway(choice.Fixture.Build()) { OnCall = (_, _) => NodeOk(("out", "chosen")) };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(choice.PipelineId, JsonSerializer.SerializeToElement(new { strategy = "by hand" }), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Envelopes.Single().Envelope.GetProperty("inputs").GetProperty("strategy").GetString().Should().Be("by hand");
    }

    [Fact]
    public async Task RunAsync_AWireIntoThePort_WinsOverTheThingChosen()
    {
        var choice = TestGraphs.ChoicePipeline(wiredFromASource: true);
        choice.Fixture.Rel(choice.Node, choice.Offers, choice.Offered[1]);
        var gateway = new FakeGateway(choice.Fixture.Build())
        {
            OnCall = (subdomain, _) => subdomain == "source" ? NodeOk(("out", "from the wire")) : NodeOk(("out", "chosen")),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(choice.PipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Envelopes.Single(e => e.Subdomain == "choose").Envelope.GetProperty("inputs").GetProperty("strategy").GetString().Should().Be("from the wire");
    }

    [Fact]
    public async Task RunAsync_AChoiceNoPortOffers_IsRefusedBeforeAnythingRuns()
    {
        var choice = TestGraphs.ChoicePipeline();
        var stranger = choice.Fixture.Thing("At random");
        choice.Fixture.Rel(choice.Node, choice.Offers, stranger);
        var gateway = new FakeGateway(choice.Fixture.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(choice.PipelineId, default, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Choose").And.Contain("At random").And.Contain("no in port of it offers");
        gateway.Called.Should().BeEmpty();
        gateway.RunError.Should().Be(result.Error);
    }

    [Fact]
    public async Task RunAsync_SeveralWiresIntoOneCollectionInput_GatherAsOneListInWireOrder()
    {
        var (fx, pipelineId) = TestGraphs.GatheredCollectionPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (subdomain, _) => subdomain switch
            {
                "a" => NodeAnswering("""{"out":["a1","a2"]}"""),
                "b" => NodeAnswering("""{"out":["b1"]}"""),
                "gather" => NodeOk(("out", "gathered")),
                _ => NodeFail("unexpected subdomain"),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        ItemsSentTo(gateway, "gather").Should().Equal("a1", "a2", "b1");
    }

    [Fact]
    public async Task RunAsync_AWireFromAPortTheAnswerLeftOut_AddsANullToACollectionInput()
    {
        var (fx, pipelineId) = TestGraphs.GatheredCollectionPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (subdomain, _) => subdomain switch
            {
                "a" => NodeOk(),
                "b" => NodeAnswering("""{"out":["b1"]}"""),
                _ => NodeOk(("out", "gathered")),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        ItemsSentTo(gateway, "gather").Should().Equal(null, "b1");
    }

    private static IEnumerable<string?> ItemsSentTo(FakeGateway gateway, string subdomain) =>
        gateway.Envelopes.Where(e => e.Subdomain == subdomain)
            .OrderBy(e => e.Envelope.GetProperty("index").GetInt32())
            .Select(e => e.Envelope.GetProperty("inputs").GetProperty("items").GetString());

    [Fact]
    public async Task RunAsync_AScalarWireAndABoundScalar_EachCountAsOneItemOfACollectionInput()
    {
        var (fx, pipelineId) = TestGraphs.GatheredCollectionPipeline(boundTo: "first");
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (subdomain, _) => subdomain switch
            {
                "a" => NodeOk(("out", "a1")),
                "b" => NodeAnswering("""{"out":["b1","b2"]}"""),
                _ => NodeOk(("out", "gathered")),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, JsonSerializer.SerializeToElement(new { first = "f0" }), CancellationToken.None);

        result.Success.Should().BeTrue();
        ItemsSentTo(gateway, "gather").Should().Equal("f0", "a1", "b1", "b2");
    }

    [Fact]
    public async Task RunAsync_FanOut_ACollectionOutPortDeliversOneFlatListAlongItsWire()
    {
        var (fx, pipelineId) = TestGraphs.FanOutWithCollectionOutPipeline("continue");
        var end = fx.Get("FanDemo end");
        var reached = fx.Thing("end.parts", ("direction", "in"), ("type", "any"), ("portName", "parts"));
        fx.Rel(reached, fx.Get("is"), fx.Get("Port"));
        fx.Rel(end, fx.Get("has"), reached);
        fx.Wire(fx.Get("Scorer"), "parts", end, "parts");
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (_, envelope) => envelope.GetProperty("inputs").GetProperty("item").GetString() switch
            {
                "a" => NodeAnswering("""{"score":"A","parts":["a1","a2"]}"""),
                "b" => NodeFail("boom"),
                "c" => NodeAnswering("""{"score":"C","parts":"c1"}"""),
                _ => NodeAnswering("""{"score":"D"}"""),
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c", "d" }, w = 1 }), CancellationToken.None);

        result.Result!.Value.GetProperty("parts").EnumerateArray().Select(e => e.GetString()).Should().Equal("a1", "a2", "c1");
    }

    [Fact]
    public async Task RunAsync_FanOut_ACollectionOutPortGathersFlat_AndAnotherPortPerItem()
    {
        var (fx, pipelineId) = TestGraphs.FanOutWithCollectionOutPipeline("continue");
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (_, envelope) =>
            {
                var item = envelope.GetProperty("inputs").GetProperty("item").GetString()!;
                return item switch
                {
                    "a" => NodeAnswering("""{"score":"A","parts":["a1","a2"]}"""),
                    "b" => NodeFail("boom"),
                    "c" => NodeAnswering("""{"score":"C","parts":"c1"}"""),
                    _ => NodeAnswering("""{"score":"D"}"""),
                };
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "b", "c", "d" }, w = 1 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        var node = result.Nodes.Single(n => n.Name == "Scorer");
        node.Status.Should().Be(RunStatus.Partial);
        // a's list contributes its items, c's single value is one item, b failed and d answered nothing under the port
        node.Outputs["parts"].EnumerateArray().Select(e => e.GetString()).Should().Equal("a1", "a2", "c1");
        node.Outputs["score"].EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? null : e.GetString()).Should().Equal("A", null, "C", "D");
    }

    [Fact]
    public async Task RunAsync_FanOut_ACollectionOutPortNamedLikeAnInPort_StillGathersFlat()
    {
        var (fx, pipelineId) = TestGraphs.FanOutWithAnInPortNamedLikeItsCollectionOut();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (_, envelope) => envelope.GetProperty("inputs").GetProperty("item").GetString() == "a"
                ? NodeAnswering("""{"score":"A","parts":["a1","a2"]}""")
                : NodeAnswering("""{"score":"C","parts":"c1"}"""),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var runParams = JsonSerializer.SerializeToElement(new { items = new[] { "a", "c" }, w = 1 });

        var result = await executor.RunAsync(pipelineId, runParams, CancellationToken.None);

        result.Nodes.Single(n => n.Name == "Scorer").Outputs["parts"].EnumerateArray().Select(e => e.GetString()).Should().Equal("a1", "a2", "c1");
    }

    [Fact]
    public async Task RunAsync_RecordsTheParametersOnTheRun_AndEachNodesOutputsOnItsNodeRun()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (subdomain, _) => subdomain == "gen" ? NodeOk(("echo", "hi")) : NodeOk(("echo", "hi again")),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        await executor.RunAsync(pipelineId, JsonSerializer.SerializeToElement(new { greeting = "hello" }), CancellationToken.None,
            subject: new RunSubject(Guid.NewGuid(), "a Thing"));

        gateway.ParametersRecorded!.Value.GetProperty("greeting").GetString().Should().Be("hello");
        gateway.ParametersRecorded!.Value.GetProperty("subject").GetProperty("name").GetString().Should().Be("a Thing");
        var echoRuns = gateway.NodeRunsPersisted.Where(n => n.Name == "Echo").ToList();
        echoRuns.Select(n => n.Status).Should().Equal(RunStatus.Running, RunStatus.Succeeded);
        echoRuns[0].Outputs.Should().BeNull();
        echoRuns[1].Outputs!["echo"].GetString().Should().Be("hi again");
    }

    [Fact]
    public async Task RunAsync_RelatesTheNodeRunToEachThingAnsweredUnderAProducingPort()
    {
        var (fx, pipelineId) = TestGraphs.ProducingPipeline();
        var one = Guid.NewGuid(); var two = Guid.NewGuid(); var three = Guid.NewGuid();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (_, _) => NodeAnswering($$"""{"made":[{"id":"{{one}}","name":"first"},"{{two}}",{"id":"{{three}}"},"not an identifier",{"name":"no id"}],"note":"{{Guid.NewGuid()}}"}"""),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        result.Success.Should().BeTrue();
        var produced = gateway.Produced.Single();
        produced.NodeId.Should().Be(fx.Get("Maker").Id);
        produced.Index.Should().BeNull();
        produced.Things.Should().Equal(one, two, three);
    }

    [Fact]
    public async Task RunAsync_ANodeAnsweringNoThingUnderItsProducingPort_RelatesNothing()
    {
        var (fx, pipelineId) = TestGraphs.ProducingPipeline();
        var gateway = new FakeGateway(fx.Build()) { OnCall = (_, _) => NodeAnswering("""{"made":"a plain word","note":"x"}""") };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        await executor.RunAsync(pipelineId, default, CancellationToken.None);

        gateway.Produced.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_AFanOut_RelatesEachItemsNodeRunToWhatThatItemProduced()
    {
        var (fx, pipelineId) = TestGraphs.ProducingPipeline(perItem: true);
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var gateway = new FakeGateway(fx.Build())
        {
            OnCall = (_, envelope) => envelope.GetProperty("inputs").GetProperty("in").GetString() == "x"
                ? NodeAnswering("{\"made\":{\"id\":\"" + a + "\"}}")
                : NodeAnswering("{\"made\":{\"id\":\"" + b + "\"}}"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(pipelineId, JsonSerializer.SerializeToElement(new { items = new[] { "x", "y" } }), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Produced.OrderBy(p => p.Index).Select(p => (p.Index, p.Things.Single())).Should().Equal((0, a), (1, b));
        gateway.NodeRunsPersisted.Where(n => n.Index == 1 && n.Status == RunStatus.Succeeded).Single().Outputs!["made"].GetProperty("id").GetString().Should().Be(b.ToString());
    }

    private static NodeCallResult NodeAnswering(string outputsJson) =>
        new(200, $$"""{"result":"success","outputs":{{outputsJson}},"error":null}""");

    private static NodeCallResult NodeOk(params (string Port, string Value)[] outputs)
    {
        var outs = outputs.ToDictionary(o => o.Port, o => o.Value);
        return new NodeCallResult(200, JsonSerializer.Serialize(new { result = "success", outputs = outs, error = (string?)null }));
    }

    private static NodeCallResult NodeFail(string error) =>
        new(200, JsonSerializer.Serialize(new { result = "fail", outputs = new Dictionary<string, string>(), error }));

    private static string InputValue(JsonElement envelope, string port) =>
        envelope.GetProperty("inputs").GetProperty(port).GetString()!;
}
