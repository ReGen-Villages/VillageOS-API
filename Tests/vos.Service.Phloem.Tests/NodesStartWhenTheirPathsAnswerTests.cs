using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class NodesStartWhenTheirPathsAnswerTests
{
    private static NodeCallResult Answer() => new(200, "{\"result\":\"success\",\"outputs\":{\"out\":\"level\"}}");

    private static TaskCompletionSource<NodeCallResult> Held() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static (FakeGateway Gateway, PipelineExecutor Executor) Arrange(
        (GraphFixture Fixture, Guid PipelineId) drawing, (string Subdomain, TaskCompletionSource<NodeCallResult> Answer)[] held, int maxConcurrency = 4)
    {
        var gateway = new FakeGateway(drawing.Fixture.Build())
        {
            OnCallAsync = (subdomain, _) =>
                held.FirstOrDefault(call => call.Subdomain == subdomain).Answer?.Task ?? Task.FromResult(Answer()),
        };
        return (gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock(), maxConcurrency: maxConcurrency));
    }

    [Fact]
    public async Task A_quick_paths_next_node_starts_while_a_slow_path_is_still_calling()
    {
        var drawing = TestGraphs.TwoPathsPipeline();
        var spring = Held();
        var (gateway, executor) = Arrange(drawing, [("spring", spring)]);

        var running = executor.RunAsync(drawing.PipelineId, default, CancellationToken.None);
        try
        {
            await Settle.UntilAsync(() => gateway.CalledSoFar().Contains("record"), "'Record level' is called while 'Read spring' is still calling");
        }
        finally
        {
            spring.TrySetResult(Answer());
        }
        var result = await running;

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_node_where_two_paths_meet_starts_only_once_both_have_answered()
    {
        var drawing = TestGraphs.TwoPathsPipeline(springAlsoFeedsRecordLevel: true);
        var spring = Held();
        var (gateway, executor) = Arrange(drawing, [("spring", spring)]);

        var running = executor.RunAsync(drawing.PipelineId, default, CancellationToken.None);
        await Settle.UntilAsync(() => gateway.CalledSoFar() is var called && called.Contains("spring") && called.Contains("reservoir"),
            "both paths are calling");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        var calledWhileSpringCalls = gateway.CalledSoFar();
        spring.TrySetResult(Answer());
        var result = await running;

        calledWhileSpringCalls.Should().NotContain("record");
        result.Success.Should().BeTrue();
        gateway.CalledSoFar().Should().EndWith("record");
    }

    [Fact]
    public async Task A_stop_asked_while_both_paths_are_calling_starts_nothing_more()
    {
        var drawing = TestGraphs.TwoPathsPipeline();
        var (spring, reservoir) = (Held(), Held());
        var (gateway, executor) = Arrange(drawing, [("spring", spring), ("reservoir", reservoir)]);
        var runId = Guid.NewGuid();

        var running = executor.RunAsync(drawing.PipelineId, default, CancellationToken.None, runId: runId);
        await Settle.UntilAsync(() => gateway.CalledSoFar() is var called && called.Contains("spring") && called.Contains("reservoir"),
            "both paths are calling");
        var stopping = executor.StopAsync(Guid.NewGuid(), runId, CancellationToken.None);
        reservoir.TrySetResult(Answer());
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        spring.TrySetResult(Answer());
        await running;
        await stopping;

        gateway.CalledSoFar().Should().NotContain("record");
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Cancelled);
    }

    [Fact]
    public async Task A_run_cancelled_while_one_path_is_calling_ends_only_once_that_call_has_answered()
    {
        var drawing = TestGraphs.TwoPathsPipeline();
        var (spring, reservoir) = (Held(), Held());
        var (gateway, executor) = Arrange(drawing, [("spring", spring), ("reservoir", reservoir)], maxConcurrency: 1);
        using var cancellation = new CancellationTokenSource();

        var running = executor.RunAsync(drawing.PipelineId, default, cancellation.Token);
        await Settle.UntilAsync(() => gateway.CalledSoFar().Count == 1, "one path is calling while the other waits its turn");
        cancellation.Cancel();
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(200));
        var endedWhileCalling = running.IsCompleted;
        spring.TrySetResult(Answer());
        reservoir.TrySetResult(Answer());

        endedWhileCalling.Should().BeFalse("the run must not end while a call it started is still in progress");
        await running.Invoking(run => run).Should().ThrowAsync<OperationCanceledException>();
    }
}
