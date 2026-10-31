using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

// A node with a retry policy keeps calling a service it cannot reach, writes each attempt as it happens, and hands its
// subject to another drawing when trying ends without an answer.
public class RetryTests
{
    private static readonly RunSubject Plot = new(Guid.NewGuid(), "Plot 7");

    // By default one model minute passes in ten real milliseconds, so a pause is taken without a test that waits long.
    private static ModelClock FastClock(double rate = 6000, TimeProvider? real = null)
    {
        var clock = new ModelClock(real);
        clock.AnchorTo(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), rate);
        return clock;
    }

    private sealed class ClockBehindTheTimer : TimeProvider
    {
        private readonly DateTimeOffset _started = System.GetUtcNow();

        public override DateTimeOffset GetUtcNow() => _started + (System.GetUtcNow() - _started) / 2;
    }

    private static (TestGraphs.RetryFixture Retry, FakeGateway Gateway, PipelineExecutor Executor) Arrange(
        Func<int, NodeCallResult> answerToCall, bool bindsHandOver = false, double clockRate = 6000, TimeProvider? real = null,
        params (string Name, object Value)[] policy)
    {
        var retry = TestGraphs.RetryPipeline(bindsHandOver, policy: policy);
        var calls = 0;
        var gateway = new FakeGateway(retry.Fixture.Build())
        {
            OnCall = (_, _) => answerToCall(Interlocked.Increment(ref calls)),
        };
        return (retry, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock(clockRate, real)));
    }

    private static NodeCallResult Answer() => new(200, JsonSerializer.Serialize(new { result = "success", outputs = new { @out = "echoed" } }));

    private static NodeCallResult Unreachable() => throw new HttpRequestException("the service is away");

    private static JsonElement Seed() => JsonSerializer.SerializeToElement(new { seed = "world" });

    [Fact]
    public async Task A_node_whose_service_fails_twice_and_then_answers_completes_with_three_attempts_recorded()
    {
        var (retry, gateway, executor) = Arrange(call => call < 3 ? Unreachable() : Answer(), policy: [("attempts", 3), ("pauseMinutes", 1)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Called.Should().HaveCount(3);
        var attempts = gateway.AttemptsRecorded.Select(recorded => recorded.Attempt).ToList();
        attempts.Select(attempt => attempt.Number).Should().Equal(1, 2, 3);
        attempts.Select(attempt => attempt.Status).Should().Equal(RunStatus.Failed, RunStatus.Failed, RunStatus.Succeeded);
        attempts[0].Error.Should().Contain("the service is away");
        gateway.NodeStatuses.Count(status => status == ("Echo", RunStatus.Trying)).Should().Be(2);
        gateway.Records[(result.RunId, retry.Echo.Id)].AttemptsMade.Should().HaveCount(3);
        gateway.NodeErrorsWritten.Should().NotContain(written => written.Name == "Echo", "each attempt holds its own error, and the node run that answered has none");
    }

    // A model minute is a tenth of a real second here, well above the scheduling noise of a loaded machine, and only
    // lower bounds are asserted, since a busy machine can only lengthen a pause.
    [Fact]
    public async Task The_pause_before_each_attempt_grows_and_is_measured_on_the_model_clock()
    {
        var (retry, gateway, executor) = Arrange(call => call < 3 ? Unreachable() : Answer(), clockRate: 600,
            policy: [("attempts", 3), ("pauseMinutes", 1), ("pauseGrowth", 3)]);

        await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        var started = gateway.AttemptsRecorded.Select(recorded => recorded.Attempt.StartedUtc).ToList();
        (started[1] - started[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
        (started[2] - started[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task A_pause_ends_when_the_model_clock_reaches_its_end_even_where_the_timer_fires_early()
    {
        var (retry, gateway, executor) = Arrange(call => call < 2 ? Unreachable() : Answer(), real: new ClockBehindTheTimer(),
            policy: [("attempts", 2), ("pauseMinutes", 1)]);

        await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        var attempts = gateway.AttemptsRecorded.Select(recorded => recorded.Attempt).ToList();
        (attempts[1].StartedUtc - attempts[0].FinishedUtc).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task A_cancelled_run_leaves_its_pause_at_once()
    {
        var (retry, gateway, executor) = Arrange(call => call < 2 ? Unreachable() : Answer(), clockRate: 600,
            policy: [("attempts", 2), ("pauseMinutes", 600)]);
        using var cancelling = new CancellationTokenSource();

        var running = executor.RunAsync(retry.PipelineId, Seed(), cancelling.Token);
        await Settle.UntilAsync(() => gateway.NodeStatuses.Contains(("Echo", RunStatus.Trying)), "the node pauses before its second attempt");
        cancelling.Cancel();

        await Settle.UntilAsync(() => running.IsCompleted, "the pause ends when the run is cancelled");
    }

    [Fact]
    public async Task A_clock_that_stands_still_takes_the_pause_once_in_real_time()
    {
        var (retry, gateway, executor) = Arrange(call => call < 2 ? Unreachable() : Answer(), clockRate: 0,
            policy: [("attempts", 2), ("pauseMinutes", 0.001)]);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Called.Should().HaveCount(2);
        watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(50),
            "the 60 millisecond pause is timed by a different clock from the stopwatch, which can see it end a fraction early");
    }

    [Fact]
    public async Task A_refusal_is_never_retried()
    {
        var (retry, gateway, executor) = Arrange(_ => new NodeCallResult(400, "{\"error\":\"no\"}"), policy: [("attempts", 3), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Called.Should().HaveCount(1);
        gateway.AttemptsRecorded.Should().ContainSingle().Which.Attempt.Status.Should().Be(RunStatus.Failed);
        gateway.NodeStatuses.Should().NotContain(("Echo", RunStatus.Trying));
    }

    [Fact]
    public async Task A_refusal_on_the_last_attempt_fails_the_node()
    {
        var (retry, gateway, executor) = Arrange(call => call < 2 ? Unreachable() : new NodeCallResult(400, "{\"error\":\"no\"}"),
            policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.Failed);
        result.Error.Should().Be("'Echo' failed, and no fail wire leaves it.");
        gateway.RunsCreated.Should().ContainSingle();
    }

    [Fact]
    public async Task An_answer_that_says_the_work_failed_is_final()
    {
        var (retry, gateway, executor) = Arrange(_ => new NodeCallResult(200, JsonSerializer.Serialize(new { result = "fail", error = "could not" })),
            policy: [("attempts", 3), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Called.Should().HaveCount(1);
        result.Nodes.Single(node => node.Name == "Echo").Error.Should().Be("could not");
    }

    [Fact]
    public async Task A_door_answering_for_a_service_it_cannot_reach_is_retried()
    {
        var (retry, gateway, executor) = Arrange(call => call < 2 ? new NodeCallResult(503, "") : Answer(), policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Called.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_node_whose_attempts_run_out_fails_and_starts_nothing_where_the_model_still_binds_hand_over()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable(), bindsHandOver: true, policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Plot);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("'Echo' failed, and no fail wire leaves it.");
        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.Failed);
        gateway.Called.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_node_whose_trying_time_runs_out_fails_before_its_attempts_do()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable(), policy: [("attempts", 5), ("pauseMinutes", 2), ("tryingMinutes", 1)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.Failed);
        gateway.Called.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_node_naming_no_drawing_fails_when_its_attempts_run_out()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable(), policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("'Echo' failed, and no fail wire leaves it.");
        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.Failed);
        gateway.Called.Should().HaveCount(2);
        gateway.RunsCreated.Should().ContainSingle();
    }

    [Fact]
    public async Task A_node_that_runs_once_per_item_runs_where_the_model_still_binds_hand_over()
    {
        var retry = TestGraphs.RetryPipeline(bindsHandOver: true, perItem: true, policy: [("attempts", 2)]);
        var gateway = new FakeGateway(retry.Fixture.Build())
        {
            OnCall = (_, _) => Answer(),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock());

        var result = await executor.RunAsync(retry.PipelineId, JsonSerializer.SerializeToElement(new { seed = new[] { "a", "b" } }), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Called.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_node_without_a_policy_is_called_once_and_records_no_attempt()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable());

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Called.Should().HaveCount(1);
        gateway.AttemptsRecorded.Should().BeEmpty();
    }

    [Fact]
    public async Task A_restarted_orchestrator_carries_a_trying_node_on_from_its_record_numbering_the_next_attempt_after_the_last()
    {
        var (retry, gateway, executor) = Arrange(_ => Answer(), policy: [("attempts", 3), ("pauseMinutes", 1)]);
        var runId = Guid.NewGuid();
        var anHourAgo = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
        gateway.Runs[runId] = (retry.PipelineId, Seed());
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["seed"] = JsonSerializer.SerializeToElement("world") });
        gateway.Records[(runId, retry.Echo.Id)] = new NodeRecord(RunStatus.Trying, new Dictionary<string, JsonElement>(),
            [new AttemptRecord(1, anHourAgo, anHourAgo.AddSeconds(1), RunStatus.Failed, "the service was away")]);

        var carriedOn = await executor.RecoverTryingAsync(CancellationToken.None);

        carriedOn.Should().Be(1);
        await Settle.UntilAsync(() => gateway.StatusUpdates.Contains(RunStatus.Succeeded), "the run carries on to its end");
        gateway.Called.Should().ContainSingle();
        gateway.AttemptsRecorded.Should().ContainSingle().Which.Attempt.Number.Should().Be(2);
        gateway.Records[(runId, retry.Echo.Id)].Status.Should().Be(RunStatus.Succeeded);
    }

    [Fact]
    public async Task A_rehearsal_carried_on_after_a_restart_is_still_a_rehearsal()
    {
        var (retry, gateway, executor) = Arrange(_ => Answer(), policy: [("attempts", 3), ("pauseMinutes", 0)]);
        retry.Fixture.State(retry.Fixture.Get("Service"), ModelNames.Rehearses, true);
        gateway.Graph = retry.Fixture.Build();
        var runId = Guid.NewGuid();
        var anHourAgo = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
        await gateway.CreateRunAsync(runId, retry.PipelineId, CancellationToken.None, parameters: Seed(), rehearsal: true);
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["seed"] = JsonSerializer.SerializeToElement("world") });
        gateway.Records[(runId, retry.Echo.Id)] = new NodeRecord(RunStatus.Trying, new Dictionary<string, JsonElement>(),
            [new AttemptRecord(1, anHourAgo, anHourAgo.AddSeconds(1), RunStatus.Failed, "the service was away")]);

        await executor.RecoverTryingAsync(CancellationToken.None);

        await Settle.UntilAsync(() => gateway.StatusUpdates.Contains(RunStatus.Rehearsed), "the rehearsal carries on to its end");
        gateway.Envelopes.Single().Envelope.GetProperty(ModelNames.Rehearsal).GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_run_whose_fan_out_item_was_trying()
    {
        var (retry, gateway, executor) = Arrange(_ => Answer(), policy: [("attempts", 3), ("pauseMinutes", 1)]);
        var runId = Guid.NewGuid();
        gateway.Runs[runId] = (retry.PipelineId, Seed());
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement>());
        gateway.TryingItems.Add(new TryingNodeRun(runId, retry.Echo.Id, PerItem: true));

        var carriedOn = await executor.RecoverTryingAsync(CancellationToken.None);

        carriedOn.Should().Be(0);
        gateway.Called.Should().BeEmpty();
        gateway.RunError.Should().Be("'Echo' was trying one of its items when the orchestrator stopped, and a fan-out cannot be carried on from its record.");
    }
}
