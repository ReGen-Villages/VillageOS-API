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
        Func<int, NodeCallResult> answerToCall, bool bindsHandOver = false, double clockRate = 6000, TimeProvider? real = null, bool perItem = false,
        params (string Name, object Value)[] policy)
    {
        var retry = TestGraphs.RetryPipeline(bindsHandOver, perItem: perItem, policy: policy);
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
            "the 60-millisecond pause was taken, and its timer can end a fraction of a millisecond before a stopwatch reaches 60");
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
    public async Task The_broker_answering_for_a_service_it_cannot_reach_is_retried()
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

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

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

        await executor.RecoverUnfinishedAsync(CancellationToken.None);

        await Settle.UntilAsync(() => gateway.StatusUpdates.Contains(RunStatus.Rehearsed), "the rehearsal carries on to its end");
        gateway.Envelopes.Single().Envelope.GetProperty(ModelNames.Rehearsal).GetBoolean().Should().BeTrue();
    }

    private static (TestGraphs.RetryFixture Retry, FakeGateway Gateway, PipelineExecutor Executor, Guid RunId) ArrangeARunStoppedPartWayThroughItsItems(
        string secondItemStatus)
    {
        var (retry, gateway, executor) = Arrange(_ => Answer(), perItem: true, policy: [("attempts", 3), ("pauseMinutes", 1)]);
        var runId = Guid.NewGuid();
        var readings = JsonSerializer.SerializeToElement(new[] { "North", "South", "East" });
        var anHourAgo = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
        gateway.Runs[runId] = (retry.PipelineId, JsonSerializer.SerializeToElement(new { seed = readings }));
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["seed"] = readings });
        gateway.Records[(runId, retry.Echo.Id)] = new NodeRecord(RunStatus.Running, new Dictionary<string, JsonElement>());
        gateway.RecordItem(runId, retry.Echo.Id, new NodeRecord(RunStatus.Succeeded,
            new Dictionary<string, JsonElement> { ["out"] = JsonSerializer.SerializeToElement("North as answered before") }, Number: new ItemNumber(0, 3)));
        gateway.RecordItem(runId, retry.Echo.Id, new NodeRecord(secondItemStatus, new Dictionary<string, JsonElement>(),
            [new AttemptRecord(1, anHourAgo, anHourAgo.AddSeconds(1), RunStatus.Failed, "the service was away")], Number: new ItemNumber(1, 3)));
        return (retry, gateway, executor, runId);
    }

    private static List<string> ReadingsCalled(FakeGateway gateway)
    {
        lock (gateway.Called)
            return gateway.Envelopes.Select(call => call.Envelope.GetProperty("inputs").GetProperty("in").GetString()!).ToList();
    }

    [Fact]
    public async Task A_restarted_orchestrator_carries_on_a_run_whose_item_was_trying_calling_only_the_items_not_yet_answered()
    {
        var (retry, gateway, executor, runId) = ArrangeARunStoppedPartWayThroughItsItems(RunStatus.Trying);

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(1);
        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) is RunStatus.Succeeded or RunStatus.Failed, "the run carries on to its end");
        (gateway.RunStatusOf(runId), gateway.RunError).Should().Be((RunStatus.Succeeded, (string?)null));
        ReadingsCalled(gateway).Should().BeEquivalentTo("South", "East");
        gateway.AttemptsRecorded.Where(made => made.Index == 1).Select(made => made.Attempt.Number).Should().Equal(2);
        gateway.RunResult!.Value.GetProperty("result").EnumerateArray().Select(answer => answer.GetString())
            .Should().Equal("North as answered before", "echoed", "echoed");
    }

    [Fact]
    public async Task An_item_answered_before_the_restart_still_goes_on_to_the_next_node()
    {
        var drawing = TestGraphs.ItemsGoOnPipeline(checkRetries: true);
        var model = drawing.Fixture.Build();
        var gateway = new FakeGateway(model)
        {
            OnCall = (_, envelope) => new NodeCallResult(200,
                $"{{\"result\":\"success\",\"outputs\":{{\"out\":{envelope.GetProperty("inputs").GetProperty("in").GetRawText()}}}}}"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock());
        var (runId, input, check) = (Guid.NewGuid(), model.Things.Single(thing => thing.Name == "In").Id, model.Things.Single(thing => thing.Name == "Check reading").Id);
        var readings = JsonSerializer.SerializeToElement(new[] { "North", "South", "East" });
        var anHourAgo = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
        gateway.Runs[runId] = (drawing.PipelineId, JsonSerializer.SerializeToElement(new { readings }));
        gateway.Records[(runId, input)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["readings"] = readings });
        gateway.Records[(runId, check)] = new NodeRecord(RunStatus.Running, new Dictionary<string, JsonElement>());
        gateway.RecordItem(runId, check, new NodeRecord(RunStatus.Succeeded,
            new Dictionary<string, JsonElement> { ["out"] = JsonSerializer.SerializeToElement("North") }, Number: new ItemNumber(0, 3)));
        gateway.RecordItem(runId, check, new NodeRecord(RunStatus.Trying, new Dictionary<string, JsonElement>(),
            [new AttemptRecord(1, anHourAgo, anHourAgo.AddSeconds(1), RunStatus.Failed, "the service was away")], Number: new ItemNumber(1, 3)));

        await executor.RecoverUnfinishedAsync(CancellationToken.None);

        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) is RunStatus.Succeeded or RunStatus.Failed, "the run carries on to its end");
        List<string> Called(string subdomain)
        {
            lock (gateway.Called)
                return gateway.Envelopes.Where(call => call.Subdomain == subdomain).Select(call => call.Envelope.GetProperty("inputs").GetProperty("in").GetString()!).ToList();
        }
        Called("check").Should().BeEquivalentTo("South", "East");
        Called("record").Should().BeEquivalentTo("North", "South", "East");
    }

    [Fact]
    public async Task A_restarted_orchestrator_does_not_carry_on_a_run_whose_item_was_in_the_middle_of_a_call()
    {
        var (retry, gateway, executor, runId) = ArrangeARunStoppedPartWayThroughItsItems(RunStatus.Running);
        gateway.RecordItem(runId, retry.Echo.Id, new NodeRecord(RunStatus.Trying, new Dictionary<string, JsonElement>(), Number: new ItemNumber(2, 3)));

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(0);
        gateway.Called.Should().BeEmpty();
        gateway.RunError.Should().Be("'Echo' was calling for item 2 of 3 when the orchestrator stopped, and that call may have reached its service, " +
                                     "which does not say it may be called again, so the run cannot be carried on without calling it again.");
    }

    [Fact]
    public async Task A_restarted_orchestrator_calls_an_item_caught_in_the_middle_of_a_call_again_when_its_service_may_be_called_again()
    {
        var (retry, gateway, executor, runId) = ArrangeARunStoppedPartWayThroughItsItems(RunStatus.Running);
        MayBeCalledAgain(retry, gateway);

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(1);
        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) is RunStatus.Succeeded or RunStatus.Failed, "the run carries on to its end");
        (gateway.RunStatusOf(runId), gateway.RunError).Should().Be((RunStatus.Succeeded, (string?)null));
        ReadingsCalled(gateway).Should().BeEquivalentTo("South", "East");
    }

    private static (TestGraphs.RetryFixture Retry, FakeGateway Gateway, PipelineExecutor Executor, Guid RunId) ArrangeARunCaughtInTheMiddleOfACall()
    {
        var (retry, gateway, executor) = Arrange(_ => Answer());
        var runId = Guid.NewGuid();
        gateway.Runs[runId] = (retry.PipelineId, Seed());
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["seed"] = JsonSerializer.SerializeToElement("world") });
        gateway.Records[(runId, retry.Echo.Id)] = new NodeRecord(RunStatus.Running, new Dictionary<string, JsonElement>());
        return (retry, gateway, executor, runId);
    }

    private static void MayBeCalledAgain(TestGraphs.RetryFixture retry, FakeGateway gateway)
    {
        retry.Fixture.State(retry.Fixture.Get("Service"), ModelNames.MayBeCalledAgain, true);
        gateway.Graph = retry.Fixture.Build();
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_run_whose_node_was_in_the_middle_of_a_call_with_nothing_paused_instead_of_leaving_it_running()
    {
        var (_, gateway, executor, runId) = ArrangeARunCaughtInTheMiddleOfACall();

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(0);
        gateway.Called.Should().BeEmpty();
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Failed);
        gateway.RunError.Should().Be("'Echo' was calling its service when the orchestrator stopped, and that call may have reached its service, " +
                                     "which does not say it may be called again, so the run cannot be carried on without calling it again.");
    }

    [Fact]
    public async Task A_restarted_orchestrator_calls_a_node_caught_in_the_middle_of_a_call_again_when_its_service_may_be_called_again()
    {
        var (retry, gateway, executor, runId) = ArrangeARunCaughtInTheMiddleOfACall();
        MayBeCalledAgain(retry, gateway);

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(1);
        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) is RunStatus.Succeeded or RunStatus.Failed, "the run carries on to its end");
        (gateway.RunStatusOf(runId), gateway.RunError).Should().Be((RunStatus.Succeeded, (string?)null));
        gateway.Called.Should().ContainSingle();
    }

    [Fact]
    public async Task A_restarted_orchestrator_leaves_a_finished_run_alone_though_one_of_its_node_runs_still_says_running()
    {
        var (retry, gateway, executor, runId) = ArrangeARunCaughtInTheMiddleOfACall();
        MayBeCalledAgain(retry, gateway);
        gateway.RunStatuses[runId] = RunStatus.Succeeded;

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(0);
        gateway.Called.Should().BeEmpty();
        (gateway.RunStatusOf(runId), gateway.RunError).Should().Be((RunStatus.Succeeded, (string?)null));
    }

    [Fact]
    public async Task A_restarted_orchestrator_runs_a_start_node_caught_in_the_middle_of_its_step_again_since_it_calls_nothing()
    {
        var (retry, gateway, executor) = Arrange(_ => Answer());
        var runId = Guid.NewGuid();
        gateway.Runs[runId] = (retry.PipelineId, Seed());
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Running, new Dictionary<string, JsonElement>());

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(1);
        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) is RunStatus.Succeeded or RunStatus.Failed, "the run carries on to its end");
        (gateway.RunStatusOf(runId), gateway.RunError).Should().Be((RunStatus.Succeeded, (string?)null));
        gateway.Called.Should().ContainSingle();
    }

    [Fact]
    public async Task A_restarted_orchestrator_fails_a_run_whose_tell_node_was_in_the_middle_of_its_step_since_the_message_may_be_sent()
    {
        var telling = TestGraphs.TellingPipeline();
        var model = telling.Fixture.Build();
        var gateway = new FakeGateway(model);
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock());
        var runId = Guid.NewGuid();
        Guid Named(string name) => model.Things.Single(thing => thing.Name == name).Id;
        var world = new Dictionary<string, JsonElement> { ["seed"] = JsonSerializer.SerializeToElement("world") };
        gateway.Runs[runId] = (telling.PipelineId, Seed());
        gateway.Records[(runId, Named("In"))] = new NodeRecord(RunStatus.Succeeded, world);
        gateway.Records[(runId, Named("Echo"))] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["echo"] = world["seed"] });
        gateway.Records[(runId, Named(TestGraphs.TellNode))] = new NodeRecord(RunStatus.Running, new Dictionary<string, JsonElement>());

        var carriedOn = await executor.RecoverUnfinishedAsync(CancellationToken.None);

        carriedOn.Should().Be(0);
        gateway.MessagesSent.Should().BeEmpty();
        gateway.RunError.Should().Be("'Tell the office' was running when the orchestrator stopped, and what it does may already be done, " +
                                     "so the run cannot be carried on without doing it again.");
    }

    [Fact]
    public void A_service_may_be_called_again_when_it_or_a_kind_above_it_says_so()
    {
        var (fx, pipelineId) = TestGraphs.DemoPipeline();
        fx.State(fx.Get("EchoProto"), ModelNames.MayBeCalledAgain, true);

        var dag = PipelineDagBuilder.Build(fx.Build(), pipelineId);

        dag.Node(fx.Get("Echo").Id)!.MayBeCalledAgain.Should().BeTrue();
        dag.Node(fx.Get("Generate").Id)!.MayBeCalledAgain.Should().BeFalse();
    }

    private static async Task<(TestGraphs.RetryFixture Retry, FakeGateway Gateway, PipelineExecutor Executor, Guid RunId)> ARunARestartStoppedAsync()
    {
        var (retry, gateway, executor, runId) = ArrangeARunCaughtInTheMiddleOfACall();
        await executor.RecoverUnfinishedAsync(CancellationToken.None);
        return (retry, gateway, executor, runId);
    }

    [Fact]
    public async Task A_restart_that_cannot_carry_a_run_on_marks_the_node_it_caught_interrupted()
    {
        var (retry, gateway, _, runId) = await ARunARestartStoppedAsync();

        gateway.RunStatusOf(runId).Should().Be(RunStatus.Failed);
        gateway.Records[(runId, retry.Echo.Id)].Status.Should().Be(RunStatus.Interrupted);
    }

    [Fact]
    public async Task A_person_carrying_on_a_run_a_restart_stopped_calls_the_interrupted_node_again_and_the_run_finishes()
    {
        var (_, gateway, executor, runId) = await ARunARestartStoppedAsync();

        var refusal = await executor.CarryOnAsync(runId, CancellationToken.None);

        refusal.Should().BeNull();
        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) is RunStatus.Succeeded or RunStatus.Failed, "the run carries on to its end");
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Succeeded);
        gateway.RunError.Should().BeEmpty("the reason the restart gave no longer holds");
        gateway.Called.Should().ContainSingle();
    }

    [Fact]
    public async Task A_person_carrying_on_a_run_a_restart_stopped_part_way_through_its_items_calls_only_the_items_not_answered()
    {
        var (_, gateway, executor, runId) = ArrangeARunStoppedPartWayThroughItsItems(RunStatus.Running);
        await executor.RecoverUnfinishedAsync(CancellationToken.None);

        (await executor.CarryOnAsync(runId, CancellationToken.None)).Should().BeNull();

        await Settle.UntilAsync(() => gateway.RunStatusOf(runId) is RunStatus.Succeeded or RunStatus.Failed, "the run carries on to its end");
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Succeeded);
        ReadingsCalled(gateway).Should().BeEquivalentTo("South", "East");
    }

    [Theory]
    [InlineData(RunStatus.Succeeded)]
    [InlineData(RunStatus.Failed)]
    public async Task A_run_no_restart_stopped_is_neither_carried_on_nor_started_again(string status)
    {
        var (retry, gateway, executor) = Arrange(_ => Answer());
        var runId = Guid.NewGuid();
        gateway.Runs[runId] = (retry.PipelineId, Seed());
        gateway.RunStatuses[runId] = status;
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["seed"] = JsonSerializer.SerializeToElement("world") });
        gateway.Records[(runId, retry.Echo.Id)] = new NodeRecord(status, new Dictionary<string, JsonElement>());

        var carryOn = await executor.CarryOnAsync(runId, CancellationToken.None);
        var (startedAgain, startAgain) = await executor.StartAgainAsync(runId, CancellationToken.None);

        var refused = $"Run {runId} is not one a restart of the orchestrator stopped part way through a call.";
        (carryOn, startAgain, startedAgain).Should().Be((refused, refused, (Guid?)null));
        gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_whose_record_has_lost_a_node_is_not_carried_on_and_the_person_is_told_why()
    {
        var (retry, gateway, executor, runId) = await ARunARestartStoppedAsync();
        gateway.Records.Remove((runId, retry.In.Id));

        var refusal = await executor.CarryOnAsync(runId, CancellationToken.None);

        refusal.Should().Be("The record of 'In' is missing, so the run cannot be carried on without running it again.");
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Failed);
        gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_whose_pipeline_can_no_longer_be_read_is_not_carried_on_and_the_person_is_told_why()
    {
        var (retry, gateway, executor, runId) = await ARunARestartStoppedAsync();
        gateway.Graph = TestGraphs.DemoPipeline().Fixture.Build();

        var refusal = await executor.CarryOnAsync(runId, CancellationToken.None);

        refusal.Should().Be($"Pipeline {retry.PipelineId} not found in the loaded subgraph.");
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Failed);
        gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_already_being_carried_on_is_not_carried_on_a_second_time_while_its_interrupted_node_is_still_to_be_called()
    {
        var (retry, gateway, executor, runId) = await ARunARestartStoppedAsync();
        gateway.RunStatuses[runId] = RunStatus.Running;

        var refusal = await executor.CarryOnAsync(runId, CancellationToken.None);

        refusal.Should().Be($"Run {runId} is not one a restart of the orchestrator stopped part way through a call.");
        gateway.Records[(runId, retry.Echo.Id)].Status.Should().Be(RunStatus.Interrupted);
        gateway.Called.Should().BeEmpty();
    }

    [Fact]
    public async Task A_person_starting_a_run_a_restart_stopped_again_gets_a_fresh_run_of_its_pipeline_with_its_parameters_naming_the_run_it_repeats()
    {
        var (retry, gateway, executor, runId) = await ARunARestartStoppedAsync();

        var (startedAgain, refusal) = await executor.StartAgainAsync(runId, CancellationToken.None);

        refusal.Should().BeNull();
        startedAgain.Should().NotBe(runId);
        await Settle.UntilAsync(() => gateway.RunStatusOf(startedAgain!.Value) is RunStatus.Succeeded or RunStatus.Failed, "the fresh run runs to its end");
        gateway.RunStatusOf(startedAgain!.Value).Should().Be(RunStatus.Succeeded);
        gateway.Runs[startedAgain.Value].PipelineId.Should().Be(retry.PipelineId);
        gateway.Runs[startedAgain.Value].Parameters.GetProperty("seed").GetString().Should().Be("world");
        var fresh = gateway.RunsCreated.Single(run => run.RunId == startedAgain.Value);
        (fresh.Repeats, fresh.StartedBy).Should().Be(((Guid?)runId, (Guid?)null), "a run started again is no follow-on of the run it repeats");
        gateway.RunStatusOf(runId).Should().Be(RunStatus.Failed, "the run it repeats is left as it was");
    }

    [Fact]
    public async Task A_run_a_person_started_again_is_neither_started_again_nor_carried_on_and_the_person_is_told_which_run_repeats_it()
    {
        var (_, gateway, executor, runId) = await ARunARestartStoppedAsync();
        var (startedAgain, _) = await executor.StartAgainAsync(runId, CancellationToken.None);
        await Settle.UntilAsync(() => gateway.RunStatusOf(startedAgain!.Value) is RunStatus.Succeeded or RunStatus.Failed, "the fresh run runs to its end");

        var (secondStart, startAgain) = await executor.StartAgainAsync(runId, CancellationToken.None);
        var carryOn = await executor.CarryOnAsync(runId, CancellationToken.None);

        var refused = $"Run {runId} was started again as run {startedAgain}.";
        (startAgain, carryOn, secondStart).Should().Be((refused, refused, (Guid?)null));
        gateway.RunsCreated.Should().ContainSingle(run => run.Repeats == runId);
        gateway.Called.Should().ContainSingle("only the fresh run called its service");
    }

    [Fact]
    public async Task A_person_starting_a_run_again_is_answered_only_once_the_fresh_run_is_recorded_as_repeating_it()
    {
        var (_, gateway, executor, runId) = await ARunARestartStoppedAsync();
        var recording = new TaskCompletionSource();
        gateway.RunCreationHeldUntil = recording.Task;

        var asked = executor.StartAgainAsync(runId, CancellationToken.None);
        await Settle.UntilAsync(() => gateway.RunCreationStarted, "the fresh run is being recorded");
        await Settle.BeforeAssertingAbsenceAsync(TimeSpan.FromMilliseconds(100));
        var answeredBeforeRecorded = asked.IsCompleted;
        recording.SetResult();
        var (startedAgain, _) = await asked;

        answeredBeforeRecorded.Should().BeFalse();
        gateway.RunsCreated.Should().Contain(run => run.RunId == startedAgain && run.Repeats == runId);
    }

    [Fact]
    public async Task A_run_whose_pipeline_can_no_longer_be_read_is_not_started_again_and_nothing_is_recorded_as_repeating_it()
    {
        var (retry, gateway, executor, runId) = await ARunARestartStoppedAsync();
        gateway.Graph = TestGraphs.DemoPipeline().Fixture.Build();

        var (startedAgain, refusal) = await executor.StartAgainAsync(runId, CancellationToken.None);

        (startedAgain, refusal).Should().Be(((Guid?)null, $"Pipeline {retry.PipelineId} not found in the loaded subgraph."));
        gateway.RunsCreated.Should().NotContain(run => run.Repeats == runId, "the run stays listed, to be started again once its pipeline reads");
    }

    [Fact]
    public async Task A_run_whose_fresh_run_cannot_be_recorded_is_not_started_again_and_the_person_is_told_why()
    {
        var (_, gateway, executor, runId) = await ARunARestartStoppedAsync();
        gateway.RunCreationFailure = new InvalidOperationException("The platform refused the fresh run.");

        var (startedAgain, refusal) = await executor.StartAgainAsync(runId, CancellationToken.None);
        gateway.RunCreationFailure = null;
        var (later, _) = await executor.StartAgainAsync(runId, CancellationToken.None);

        (startedAgain, refusal).Should().Be(((Guid?)null, "The platform refused the fresh run."));
        later.Should().NotBeNull("a refused ask leaves the run to be started again");
    }

    [Fact]
    public async Task Two_people_asking_at_once_to_start_a_run_again_or_carry_it_on_get_one_run_going_and_the_rest_refused()
    {
        var (_, gateway, executor, runId) = await ARunARestartStoppedAsync();
        var recording = new TaskCompletionSource();
        gateway.RunCreationHeldUntil = recording.Task;

        var first = executor.StartAgainAsync(runId, CancellationToken.None).ContinueWith(ask => ask.Result.Refusal);
        await Settle.UntilAsync(() => gateway.RunCreationStarted, "the first ask is recording its fresh run");
        var second = executor.StartAgainAsync(runId, CancellationToken.None).ContinueWith(ask => ask.Result.Refusal);
        var carryOn = executor.CarryOnAsync(runId, CancellationToken.None);
        recording.SetResult();
        var asks = await Task.WhenAll(first, second, carryOn);

        asks.Count(refusal => refusal is null).Should().Be(1);
        await Settle.UntilAsync(() => gateway.Called.Count == 1, "the one run going calls the interrupted node's service");
        var going = gateway.RunsCreated.Count(run => run.Repeats == runId) + (gateway.RunStatusOf(runId) == RunStatus.Failed ? 0 : 1);
        going.Should().Be(1);
    }
}
