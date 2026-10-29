using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// The broker sends each stop request it is written back to the orchestrator, which the fake does at once.
public class StopNodeTests
{
    private static readonly RunSubject Reservoir = new(Guid.NewGuid(), "Reservoir 7");
    private static readonly RunSubject Spring = new(Guid.NewGuid(), "Spring 3");

    private static (TestGraphs.StoppingFixture Stopping, FakeGateway Gateway, PipelineExecutor Executor) Arrange(bool bindsRequestedBy = true)
    {
        var stopping = TestGraphs.StoppingPipeline(bindsRequestedBy);
        var gateway = new FakeGateway(stopping.Fixture.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        gateway.OnStopRequestWritten = written => _ = executor.StopAsync(written.RequestId, written.ThingId, CancellationToken.None);
        return (stopping, gateway, executor);
    }

    private static JsonElement Asked(object things, string reason = "the site is retired") =>
        JsonSerializer.SerializeToElement(new { things, reason });

    private static object Named(RunSubject thing) => new { id = thing.Id.ToString(), name = thing.Name };

    [Fact]
    public async Task A_stop_node_writes_one_request_per_Thing_carrying_the_reason_and_naming_its_run_and_answers_how_many()
    {
        var (stopping, gateway, executor) = Arrange();

        var result = await executor.RunAsync(stopping.PipelineId, Asked(new[] { Named(Reservoir), Named(Spring) }), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.StopRequestsWritten.Select(written => (written.RunId, written.ThingId, written.Reason))
            .Should().BeEquivalentTo([(result.RunId, Reservoir.Id, "the site is retired"), (result.RunId, Spring.Id, "the site is retired")]);
        result.Nodes.Single(node => node.Name == TestGraphs.StopNode).Outputs[ModelNames.Requested].GetInt32().Should().Be(2);
        result.Result!.Value.GetProperty(ModelNames.Requested).GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_stop_node_given_one_Thing_rather_than_a_list_writes_one_request()
    {
        var (stopping, gateway, executor) = Arrange();

        var result = await executor.RunAsync(stopping.PipelineId, Asked(Named(Reservoir)), CancellationToken.None);

        gateway.StopRequestsWritten.Should().ContainSingle().Which.ThingId.Should().Be(Reservoir.Id);
        result.Nodes.Single(node => node.Name == TestGraphs.StopNode).Outputs[ModelNames.Requested].GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Every_other_run_about_those_Things_is_stopped_but_the_run_holding_the_node_carries_on_to_its_Output_node()
    {
        var (stopping, gateway, executor) = Arrange();
        var other = Guid.NewGuid();
        await gateway.CreateRunAsync(other, stopping.PipelineId, CancellationToken.None, subject: Reservoir);

        var result = await executor.RunAsync(stopping.PipelineId, Asked(Named(Reservoir)), CancellationToken.None, subject: Reservoir);

        await Settle.UntilAsync(() => gateway.RunStatuses[other] == RunStatus.Cancelled, "the other run about the Thing is stopped");
        result.Success.Should().BeTrue();
        gateway.RunStatuses[result.RunId].Should().Be(RunStatus.Succeeded);
        result.Nodes.Single(node => node.Name == "Out").Status.Should().Be(RunStatus.Succeeded);
        gateway.StoppedBy.Should().ContainSingle().Which.RunId.Should().Be(other);
    }

    [Fact]
    public async Task A_request_that_cannot_be_written_fails_the_node_naming_the_Thing_keeps_the_others_and_takes_the_fail_path()
    {
        var (stopping, gateway, executor) = Arrange();
        var fx = stopping.Fixture;
        var heard = fx.Thing("Out.in.error", ("direction", "in"), ("type", "string"), ("portName", "error"));
        fx.Rel(heard, fx.Get("is"), fx.Get("Port"));
        fx.Rel(stopping.Output, fx.Get("has"), heard);
        fx.TakeOn(fx.WireBetween(stopping.Stop, fx.Get("error"), stopping.Output, heard), fx.Get("fail"));
        gateway.Graph = fx.Build();
        gateway.StopRequestWriteFailsFor.Add(Spring.Id);

        var result = await executor.RunAsync(stopping.PipelineId, Asked(new[] { Named(Reservoir), Named(Spring) }), CancellationToken.None);

        var stop = result.Nodes.Single(node => node.Name == TestGraphs.StopNode);
        stop.Status.Should().Be(RunStatus.Failed);
        stop.Error.Should().Contain("'Spring 3'").And.NotContain("Reservoir 7");
        gateway.StopRequestsWritten.Should().ContainSingle().Which.ThingId.Should().Be(Reservoir.Id);
        result.Nodes.Single(node => node.Name == "Out").Status.Should().Be(RunStatus.Succeeded);
        result.Error.Should().Be("A fail path reached 'Out'.");
    }

    [Fact]
    public async Task A_value_naming_no_Thing_fails_the_node_naming_the_value()
    {
        var (stopping, gateway, executor) = Arrange();

        var result = await executor.RunAsync(stopping.PipelineId, Asked(new object[] { Named(Reservoir), "the old reservoir" }), CancellationToken.None);

        result.Nodes.Single(node => node.Name == TestGraphs.StopNode).Error.Should().Contain("the old reservoir");
        gateway.StopRequestsWritten.Should().ContainSingle().Which.ThingId.Should().Be(Reservoir.Id);
    }

    [Fact]
    public async Task A_stop_node_whose_inputs_cannot_be_put_together_fails_saying_so_and_writes_nothing()
    {
        var (stopping, gateway, executor) = Arrange();
        stopping.Fixture.State(stopping.ThingsWire, ModelNames.Transform, "$error(\"no shape for the Things\")");
        gateway.Graph = stopping.Fixture.Build();

        var result = await executor.RunAsync(stopping.PipelineId, Asked(Named(Reservoir)), CancellationToken.None);

        var stop = result.Nodes.Single(node => node.Name == TestGraphs.StopNode);
        stop.Status.Should().Be(RunStatus.Failed);
        stop.Error.Should().Contain("could not be put together").And.Contain("no shape for the Things");
        gateway.StopRequestsWritten.Should().BeEmpty();
    }

    [Fact]
    public async Task A_rehearsal_writes_no_request_and_carries_on()
    {
        var (stopping, gateway, executor) = Arrange();

        var result = await executor.RunAsync(stopping.PipelineId, Asked(Named(Reservoir)), CancellationToken.None, rehearsal: true);

        gateway.StopRequestsWritten.Should().BeEmpty();
        result.Nodes.Single(node => node.Name == TestGraphs.StopNode).Status.Should().Be(RunStatus.Rehearsed);
        result.Nodes.Single(node => node.Name == "Out").Status.Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public void A_stop_node_in_a_model_binding_nothing_to_requestedBy_is_refused_when_the_pipeline_is_read()
    {
        var stopping = TestGraphs.StoppingPipeline(bindsRequestedBy: false);

        PipelineDagBuilder.Build(stopping.Fixture.Build(), stopping.PipelineId).Problems
            .Should().ContainMatch($"*'{TestGraphs.StopNode}' stops runs, but the model binds nothing to the role requestedBy*");
    }

    [Fact]
    public void A_stop_node_is_read_as_one_with_the_ports_its_kind_declares_and_the_error_port()
    {
        var stopping = TestGraphs.StoppingPipeline();

        var dag = PipelineDagBuilder.Build(stopping.Fixture.Build(), stopping.PipelineId);

        dag.Problems.Should().BeEmpty();
        var stop = dag.Node(stopping.Stop.Id)!;
        stop.Kind.Should().Be(DagNodeKind.Stop);
        stop.Ports.Select(port => port.PortName).Should().BeEquivalentTo(ModelNames.Things, ModelNames.Reason, ModelNames.Requested, "error");
    }
}
