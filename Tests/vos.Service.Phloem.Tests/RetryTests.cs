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
    private static ModelClock FastClock(double rate = 6000)
    {
        var clock = new ModelClock();
        clock.AnchorTo(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), rate);
        return clock;
    }

    private static (TestGraphs.RetryFixture Retry, FakeGateway Gateway, PipelineExecutor Executor) Arrange(
        Func<int, NodeDispatchResult> answerToCall, bool handsOver = false, double clockRate = 6000, params (string Name, object Value)[] policy)
    {
        var retry = TestGraphs.RetryPipeline(handsOver, policy: policy);
        var calls = 0;
        var gateway = new FakeGateway(retry.Fixture.Build())
        {
            OnDispatch = (_, _) => answerToCall(Interlocked.Increment(ref calls)),
        };
        return (retry, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock(clockRate)));
    }

    private static NodeDispatchResult Answer() => new(200, JsonSerializer.Serialize(new { success = true, outputs = new { @out = "echoed" } }));

    private static NodeDispatchResult Unreachable() => throw new HttpRequestException("the service is away");

    private static JsonElement Seed() => JsonSerializer.SerializeToElement(new { seed = "world" });

    [Fact]
    public async Task A_node_whose_service_fails_twice_and_then_answers_completes_with_three_attempts_recorded()
    {
        var (retry, gateway, executor) = Arrange(call => call < 3 ? Unreachable() : Answer(), policy: [("attempts", 3), ("pauseMinutes", 1)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Dispatched.Should().HaveCount(3);
        var attempts = gateway.AttemptsRecorded.Select(recorded => recorded.Attempt).ToList();
        attempts.Select(attempt => attempt.Number).Should().Equal(1, 2, 3);
        attempts.Select(attempt => attempt.Status).Should().Equal(RunStatus.Failed, RunStatus.Failed, RunStatus.Succeeded);
        attempts[0].Error.Should().Contain("the service is away");
        gateway.NodeStatuses.Count(status => status == ("Echo", RunStatus.Trying)).Should().Be(2);
        gateway.Records[(result.RunId, retry.Echo.Id)].AttemptsMade.Should().HaveCount(3);
        gateway.NodeErrorsWritten.Should().NotContain(written => written.Name == "Echo", "each attempt holds its own error, and the node run that answered has none");
    }

    [Fact]
    public async Task A_run_where_one_node_failed_and_another_handed_over_ends_failed()
    {
        var retry = TestGraphs.RetryPipeline(handsOver: true, besideAFailingNode: true, policy: [("attempts", 1)]);
        var gateway = new FakeGateway(retry.Fixture.Build())
        {
            OnDispatch = (subdomain, _) => subdomain == "ech" ? Unreachable() : new NodeDispatchResult(200, "{\"success\":false,\"error\":\"broken\"}"),
        };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock());

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.HandedOver);
        result.Error.Should().Be("One or more nodes failed.");
        gateway.StatusUpdates.Should().Contain(RunStatus.Failed).And.NotContain(RunStatus.HandedOver);
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
    public async Task A_refusal_is_never_retried()
    {
        var (retry, gateway, executor) = Arrange(_ => new NodeDispatchResult(400, "{\"error\":\"no\"}"), policy: [("attempts", 3), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Dispatched.Should().HaveCount(1);
        gateway.AttemptsRecorded.Should().ContainSingle().Which.Attempt.Status.Should().Be(RunStatus.Failed);
        gateway.NodeStatuses.Should().NotContain(("Echo", RunStatus.Trying));
    }

    [Fact]
    public async Task A_refusal_on_the_last_attempt_fails_the_node_and_hands_nothing_over()
    {
        var (retry, gateway, executor) = Arrange(call => call < 2 ? Unreachable() : new NodeDispatchResult(400, "{\"error\":\"no\"}"),
            handsOver: true, policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.Failed);
        result.Error.Should().Be("One or more nodes failed.");
        gateway.RunsCreated.Should().ContainSingle();
    }

    [Fact]
    public async Task An_answer_that_says_the_work_failed_is_final()
    {
        var (retry, gateway, executor) = Arrange(_ => new NodeDispatchResult(200, JsonSerializer.Serialize(new { success = false, error = "could not" })),
            policy: [("attempts", 3), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Dispatched.Should().HaveCount(1);
        result.Nodes.Single(node => node.Name == "Echo").Error.Should().Be("could not");
    }

    [Fact]
    public async Task A_door_answering_for_a_service_it_cannot_reach_is_retried()
    {
        var (retry, gateway, executor) = Arrange(call => call < 2 ? new NodeDispatchResult(503, "") : Answer(), policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeTrue();
        gateway.Dispatched.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_node_whose_attempts_run_out_hands_its_subject_to_the_drawing_it_names()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable(), handsOver: true, policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Plot);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("The run handed its subject to another drawing.");
        var echo = result.Nodes.Single(node => node.Name == "Echo");
        echo.Status.Should().Be(RunStatus.HandedOver);
        echo.Error.Should().EndWith("The subject went to 'What follows'.");
        result.Nodes.Single(node => node.Name == "Out").Status.Should().Be(RunStatus.Skipped);
        gateway.StatusUpdates.Should().Contain(RunStatus.HandedOver);
        await Settle.UntilAsync(() => { lock (gateway.RunsCreated) return gateway.RunsCreated.Count == 2; }, "the hand-over run is recorded");
        var handedTo = gateway.RunsCreated.Last();
        handedTo.PipelineId.Should().Be(retry.NextPipelineId);
        handedTo.StartedBy.Should().Be(result.RunId);
        handedTo.Subject.Should().Be(Plot);
        var parameters = gateway.Runs[handedTo.RunId].Parameters;
        parameters.EnumerateObject().Select(parameter => parameter.Name).Should().Equal("subject", "in");
        parameters.GetProperty("in").GetString().Should().Be("world");
    }

    [Fact]
    public async Task A_node_whose_trying_time_runs_out_hands_over_before_its_attempts_do()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable(), handsOver: true,
            policy: [("attempts", 5), ("pauseMinutes", 2), ("tryingMinutes", 1)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.HandedOver);
        gateway.Dispatched.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_node_naming_no_drawing_fails_when_its_attempts_run_out()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable(), policy: [("attempts", 2), ("pauseMinutes", 0)]);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("One or more nodes failed.");
        result.Nodes.Single(node => node.Name == "Echo").Status.Should().Be(RunStatus.Failed);
        gateway.Dispatched.Should().HaveCount(2);
        gateway.RunsCreated.Should().ContainSingle();
    }

    [Fact]
    public async Task A_node_that_runs_once_per_item_and_names_a_drawing_to_hand_over_to_is_refused_before_the_run()
    {
        var retry = TestGraphs.RetryPipeline(handsOver: true, perItem: true, policy: [("attempts", 2)]);
        var gateway = new FakeGateway(retry.Fixture.Build());
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock());

        var result = await executor.RunAsync(retry.PipelineId, JsonSerializer.SerializeToElement(new { seed = new[] { "a", "b" } }), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("'Echo' hands its subject over, but runs once for each item of 'in'; a node that hands over runs once.");
        gateway.Dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task A_node_without_a_policy_is_called_once_and_records_no_attempt()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable());

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None);

        result.Success.Should().BeFalse();
        gateway.Dispatched.Should().HaveCount(1);
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
        gateway.Dispatched.Should().ContainSingle();
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
    public async Task A_rehearsal_whose_trying_ends_hands_nothing_over()
    {
        var (retry, gateway, executor) = Arrange(_ => Unreachable(), handsOver: true, policy: [("attempts", 2), ("pauseMinutes", 0)]);
        retry.Fixture.State(retry.Fixture.Get("Service"), ModelNames.Rehearses, true);
        gateway.Graph = retry.Fixture.Build();

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, rehearsal: true);

        result.Nodes.Single(node => node.NodeId == retry.Echo.Id).Status.Should().Be(RunStatus.Failed);
        gateway.RunsCreated.Should().ContainSingle("the drawing it would hand over to is not started");
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
        gateway.Dispatched.Should().BeEmpty();
        gateway.RunError.Should().Be("'Echo' was trying one of its items when the orchestrator stopped, and a fan-out cannot be carried on from its record.");
    }
}
