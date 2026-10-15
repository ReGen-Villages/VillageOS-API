using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class StoppingRunsTests
{
    private static readonly RunSubject Plot = new(Guid.NewGuid(), "Plot 7");
    private static readonly RunSubject OtherPlot = new(Guid.NewGuid(), "Plot 8");

    private static NodeDispatchResult Answer(string port, string value) =>
        new(200, JsonSerializer.Serialize(new { success = true, outputs = new Dictionary<string, string> { [port] = value } }));

    private static (TestGraphs.WaitingFixture Waiting, FakeGateway Gateway, PipelineExecutor Executor) ArrangeWaiting()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var gateway = new FakeGateway(waiting.Fixture.Build())
        {
            OnDispatch = (subdomain, _) => subdomain == "review" ? Answer("notice", "sent for review") : Answer("out", "published"),
        };
        return (waiting, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()));
    }

    [Fact]
    public async Task A_waiting_run_stops_at_once_its_vigil_retired_and_every_node_not_finished_recording_the_reason()
    {
        var (waiting, gateway, executor) = ArrangeWaiting();
        var run = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        var vigil = gateway.VigilsPlaced.Single().VigilId;
        var request = Guid.NewGuid();

        var stopped = await executor.StopAsync(request, run.RunId, CancellationToken.None);

        stopped.Should().Be(1);
        gateway.VigilsRetired.Should().Equal(vigil);
        gateway.Awaited.Should().NotContainKey((run.RunId, waiting.Review.Id));
        gateway.Records[(run.RunId, waiting.Review.Id)].Status.Should().Be(RunStatus.Cancelled);
        gateway.Records[(run.RunId, waiting.Publish.Id)].Status.Should().Be(RunStatus.Cancelled);
        gateway.NodeErrorsWritten.Should().Contain((waiting.Publish.Name, RunStatus.Cancelled, "withdrawn by its owner"));
        gateway.RunStatuses[run.RunId].Should().Be(RunStatus.Cancelled);
        gateway.RunError.Should().Be("withdrawn by its owner");
        gateway.StoppedBy.Should().Equal((run.RunId, request));
        gateway.Records.Where(record => record.Key.RunId == run.RunId && record.Key.NodeId != waiting.Review.Id && record.Key.NodeId != waiting.Publish.Id)
            .Should().OnlyContain(record => record.Value.Status == RunStatus.Succeeded, "a node that had finished keeps how it ended");
    }

    [Fact]
    public async Task A_stop_arriving_while_a_waiting_node_is_called_ends_the_run_with_no_vigil_left()
    {
        var (waiting, gateway, executor) = ArrangeWaiting();
        Task<int>? stopping = null;
        gateway.OnDispatch = (_, envelope) =>
        {
            stopping ??= executor.StopAsync(Guid.NewGuid(), envelope.GetProperty("runId").GetGuid(), CancellationToken.None);
            return Answer("notice", "sent for review");
        };

        var run = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        await stopping!;

        gateway.LiveVigils.Should().BeEmpty();
        gateway.RunStatuses[run.RunId].Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task A_stop_arriving_while_the_last_node_is_called_leaves_the_run_finished()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        PipelineExecutor executor = null!;
        Task<int>? stopping = null;
        var gateway = new FakeGateway(fx.Build());
        gateway.OnDispatch = (subdomain, envelope) =>
        {
            if (subdomain == "ech") stopping ??= executor.StopAsync(Guid.NewGuid(), envelope.GetProperty("runId").GetGuid(), CancellationToken.None);
            return Answer("echo", "x");
        };
        executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var run = await executor.RunAsync(pipelineId, default, CancellationToken.None);
        await stopping!;

        run.Success.Should().BeTrue();
        gateway.RunStatuses[run.RunId].Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task The_answer_to_the_vigil_of_a_stopped_run_resumes_nothing()
    {
        var (waiting, gateway, executor) = ArrangeWaiting();
        var run = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        var vigil = gateway.VigilsPlaced.Single().VigilId;
        await executor.StopAsync(Guid.NewGuid(), run.RunId, CancellationToken.None);

        await executor.ResumeAsync(vigil, CancellationToken.None);

        gateway.Dispatched.Should().Equal("review");
        gateway.RunStatuses[run.RunId].Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task Stopping_the_runs_about_a_Thing_stops_each_of_them_and_leaves_the_runs_about_another()
    {
        var (waiting, gateway, executor) = ArrangeWaiting();
        var first = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        var second = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Plot);
        var other = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: OtherPlot);

        var stopped = await executor.StopAsync(Guid.NewGuid(), Plot.Id, CancellationToken.None);

        stopped.Should().Be(2);
        gateway.RunStatuses[first.RunId].Should().Be(RunStatus.Cancelled);
        gateway.RunStatuses[second.RunId].Should().Be(RunStatus.Cancelled);
        gateway.RunStatuses[other.RunId].Should().Be(RunStatus.Waiting);
        gateway.Records[(other.RunId, waiting.Review.Id)].Status.Should().Be(RunStatus.Waiting);
    }

    [Fact]
    public async Task A_run_pausing_between_attempts_stops_at_once_without_another_attempt()
    {
        var retry = TestGraphs.RetryPipeline(policy: [("attempts", 5), ("pauseMinutes", 600)]);
        var gateway = new FakeGateway(retry.Fixture.Build()) { OnDispatch = (_, _) => throw new HttpRequestException("the service is away") };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var running = executor.RunAsync(retry.PipelineId, JsonSerializer.SerializeToElement(new { seed = "world" }), CancellationToken.None);
        await Settle.UntilAsync(() => gateway.NodeStatuses.Contains(("Echo", RunStatus.Trying)), "the node pauses before its second attempt");
        var runId = gateway.RunsCreated.Single().RunId;
        var stopping = executor.StopAsync(Guid.NewGuid(), runId, CancellationToken.None);

        await Settle.ForAsync(running, "the pause ends as soon as the stop arrives");
        var result = await running;
        await stopping;

        result.Nodes.Single(node => node.NodeId == retry.Echo.Id).Status.Should().Be(RunStatus.Cancelled);
        gateway.Dispatched.Should().ContainSingle();
        gateway.RunStatuses[runId].Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task A_run_already_finished_is_left_as_it_ended()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build()) { OnDispatch = (_, _) => Answer("echo", "x") };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var run = await executor.RunAsync(pipelineId, default, CancellationToken.None);

        await executor.StopAsync(Guid.NewGuid(), run.RunId, CancellationToken.None);

        gateway.RunStatuses[run.RunId].Should().Be(RunStatus.Succeeded);
        gateway.StoppedBy.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_left_running_with_no_sweep_is_ended_here()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(fx.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var runId = Guid.NewGuid();
        await gateway.CreateRunAsync(runId, pipelineId, CancellationToken.None);

        await executor.StopAsync(Guid.NewGuid(), runId, CancellationToken.None);

        gateway.RunStatuses[runId].Should().Be(RunStatus.Cancelled);
        gateway.NodeErrorsWritten.Select(written => written.Name).Should().BeEquivalentTo("Generate", "Echo");
    }
}
