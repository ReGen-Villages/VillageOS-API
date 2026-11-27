using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Phloem.Execution;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class CallerWentAwayTests
{
    private const string Reason = "The caller waiting for the run went away while it ran.";
    private static readonly JsonElement Seed = JsonSerializer.SerializeToElement(new { seed = "world" });

    [Fact]
    public async Task Bug8736_A_waited_run_whose_connection_closes_mid_step_ends_cancelled_with_the_reason_and_frees_its_place()
    {
        var run = new WaitedRun();
        await run.UntilEchoIsCalledAsync();

        run.Caller.Cancel();

        await run.Awaiting(waited => waited.Running).Should().ThrowAsync<OperationCanceledException>();
        run.Gateway.RunStatusOf(run.RunId).Should().Be(RunStatus.Cancelled);
        run.Gateway.RunError.Should().Be(Reason);
        run.Gateway.NodeErrorsWritten.Should().Contain(("Echo", RunStatus.Cancelled, Reason));
        run.Executor.PipelinesHoldingPlaces.Should().Be(0);
    }

    [Fact]
    public async Task A_run_whose_caller_goes_away_because_the_orchestrator_stops_is_left_for_the_recovery_at_the_next_start()
    {
        var lifetime = new StoppingLifetime();
        var run = new WaitedRun(lifetime: lifetime);
        await run.UntilEchoIsCalledAsync();

        lifetime.StopApplication();
        run.Caller.Cancel();

        await run.Awaiting(waited => waited.Running).Should().ThrowAsync<OperationCanceledException>();
        run.Gateway.RunStatusOf(run.RunId).Should().Be(RunStatus.Running);
        run.Gateway.NodeStatuses.Should().NotContain(("Echo", RunStatus.Cancelled));
        run.Executor.PipelinesHoldingPlaces.Should().Be(0);
    }

    [Fact]
    public async Task Ending_writes_that_run_past_their_limit_are_one_error_and_the_place_is_still_freed()
    {
        var time = new FakeTimeProvider();
        var logged = new CapturingLogger<PipelineExecutor>();
        var run = new WaitedRun(time, logged);
        await run.UntilEchoIsCalledAsync();
        run.Gateway.RecordReadHeldUntil = new TaskCompletionSource().Task;

        run.Caller.Cancel();
        await Settle.UntilAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return run.Running.IsCompleted;
        }, "the ending writes give up at their limit");

        await run.Awaiting(waited => waited.Running).Should().ThrowAsync<OperationCanceledException>();
        logged.ErrorLines.Should().ContainSingle().Which.Should().Contain("could not be recorded cancelled within 30 s");
        run.Executor.PipelinesHoldingPlaces.Should().Be(0);
    }

    [Fact]
    public async Task Closing_writes_that_run_past_their_limit_are_one_error_and_the_run_still_returns()
    {
        var time = new FakeTimeProvider();
        var logged = new CapturingLogger<PipelineExecutor>();
        var (drawing, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(drawing.Build()) { HonoursCancellation = true, RunEndHeldUntil = new TaskCompletionSource().Task };
        var executor = new PipelineExecutor(gateway, logged, new ModelClock(), real: time);

        var running = executor.RunAsync(pipelineId, Seed, CancellationToken.None, runId: Guid.NewGuid());
        await Settle.UntilAsync(() =>
        {
            time.Advance(TimeSpan.FromSeconds(1));
            return running.IsCompleted;
        }, "the closing writes give up at their limit");

        await running;
        logged.ErrorLines.Should().ContainSingle().Which.Should().Contain("ending writes ran past 30 s");
    }

    [Fact]
    public async Task A_run_whose_caller_goes_away_while_it_writes_how_it_ended_still_records_how_it_ended()
    {
        using var caller = new CancellationTokenSource();
        var (drawing, pipelineId) = TestGraphs.DemoPipeline();
        var gateway = new FakeGateway(drawing.Build())
        {
            HonoursCancellation = true,
            OnCall = (_, _) => new NodeCallResult(500, "{}"),
            RefuseNodeRunWrite = (_, status) =>
            {
                if (status == RunStatus.Skipped) caller.Cancel();
                return false;
            },
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var runId = Guid.NewGuid();

        await executor.RunAsync(pipelineId, Seed, caller.Token, runId: runId);

        gateway.RunStatusOf(runId).Should().Be(RunStatus.Failed);
    }

    private sealed class WaitedRun
    {
        private readonly TaskCompletionSource _echoCalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WaitedRun(TimeProvider? real = null, CapturingLogger<PipelineExecutor>? logged = null, IHostApplicationLifetime? lifetime = null)
        {
            var (drawing, pipelineId) = TestGraphs.DemoPipeline();
            Gateway = new FakeGateway(drawing.Build())
            {
                HonoursCancellation = true,
                OnCallAsync = (subdomain, _) =>
                {
                    if (subdomain != "ech")
                        return Task.FromResult(new NodeCallResult(200, """{"result":"success","outputs":{"echo":"world"}}"""));
                    _echoCalled.TrySetResult();
                    return new TaskCompletionSource<NodeCallResult>().Task;
                },
            };
            Executor = new PipelineExecutor(Gateway, logged ?? (ILogger<PipelineExecutor>)NullLogger<PipelineExecutor>.Instance, new ModelClock(),
                lifetime: lifetime, real: real);
            Running = Executor.RunAsync(pipelineId, Seed, Caller.Token, runId: RunId);
        }

        public CancellationTokenSource Caller { get; } = new();
        public Guid RunId { get; } = Guid.NewGuid();
        public FakeGateway Gateway { get; }
        public PipelineExecutor Executor { get; }
        public Task<PipelineRunResult> Running { get; }

        public Task UntilEchoIsCalledAsync() => Settle.ForAsync(_echoCalled.Task, "the run calls Echo's service");
    }

    private sealed class StoppingLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
    }
}
