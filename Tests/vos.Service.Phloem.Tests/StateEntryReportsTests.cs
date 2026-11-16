using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Shared;
using vos.Service.Shared.ServiceCalls;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class StateEntryReportsTests
{
    private static readonly RunSubject Catchment = new(Guid.NewGuid(), "Catchment 3");
    private static readonly Guid OutOfDateRecord = Guid.NewGuid();

    private static NodeCallResult Answer(string port = "out", string value = "estimated") =>
        new(200, JsonSerializer.Serialize(new { result = "success", outputs = new Dictionary<string, string> { [port] = value } }));

    private static NodeCallResult Unreachable() => throw new HttpRequestException("the rainfall service is away");

    private static JsonElement Seed() => JsonSerializer.SerializeToElement(new { seed = "rainfall" });

    // One model minute passes in ten real milliseconds, so a pause between attempts is taken without a test that waits long.
    private static ModelClock FastClock()
    {
        var clock = new ModelClock();
        clock.AnchorTo(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), 6000);
        return clock;
    }

    private static (TestGraphs.RetryFixture Retry, FakeGateway Gateway, PipelineExecutor Executor) Arrange(Func<NodeCallResult> answer)
    {
        var retry = TestGraphs.RetryPipeline();
        var gateway = new FakeGateway(retry.Fixture.Build()) { OnCall = (_, _) => answer() };
        return (retry, gateway, new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock()));
    }

    private static Task ReportedItsEndAsync(FakeGateway gateway, Guid runId) =>
        Settle.UntilAsync(() => { lock (gateway.Released) return gateway.Released.Contains(runId); }, "the run's last report reaches the broker");

    [Fact]
    public async Task A_run_that_finishes_reports_received_and_then_completed()
    {
        var (retry, gateway, executor) = Arrange(() => Answer());

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await ReportedItsEndAsync(gateway, result.RunId);

        result.Success.Should().BeTrue();
        gateway.ReportsSentSoFar().Should().Equal((OutOfDateRecord, CallReport.Received), (OutOfDateRecord, CallReport.Completed));
        gateway.CallRecordsOfRuns[result.RunId].Should().Be(OutOfDateRecord);
        gateway.HeldUntilReported.Should().Contain(result.RunId);
    }

    [Fact]
    public async Task A_run_that_fails_reports_failed_with_the_reason_and_records_that_reason_on_the_run()
    {
        var (retry, gateway, executor) = Arrange(Unreachable);

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await ReportedItsEndAsync(gateway, result.RunId);

        const string reason = "'Echo' failed, and no fail wire leaves it.";
        result.Error.Should().Be(reason);
        gateway.ReportsSentSoFar().Should().Equal((OutOfDateRecord, CallReport.Received), (OutOfDateRecord, CallReport.Failed(reason)));
        gateway.RunError.Should().Be(reason, "a restart that sends this report again reads the reason from the run");
    }

    [Fact]
    public async Task A_run_refused_at_its_start_reports_failed_with_its_refusal()
    {
        var (retry, gateway, executor) = Arrange(() => Answer());
        gateway.LoadFailure = new InvalidOperationException("the drawing could not be read");

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await ReportedItsEndAsync(gateway, result.RunId);

        gateway.ReportsSentSoFar().Should().Equal((OutOfDateRecord, CallReport.Failed("Failed to load pipeline: the drawing could not be read")));
    }

    [Fact]
    public async Task A_run_that_could_not_be_recorded_still_reports_that_it_failed()
    {
        var (retry, gateway, executor) = Arrange(() => Answer());
        gateway.RunCreationFailure = new InvalidOperationException("The platform refused 'PipelineRun': the model is read-only");

        var result = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await ReportedItsEndAsync(gateway, result.RunId);

        gateway.ReportsSentSoFar().Should().Equal(
            (OutOfDateRecord, CallReport.Failed("The platform refused 'PipelineRun': the model is read-only")));
    }

    [Fact]
    public async Task A_waiting_run_that_is_stopped_reports_failed_saying_it_was_stopped()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var gateway = new FakeGateway(waiting.Fixture.Build()) { OnCall = (subdomain, _) => subdomain == "review" ? Answer("notice", "sent for review") : Answer() };
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var run = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await Settle.UntilAsync(() => gateway.ReportsSentSoFar().Count == 1, "the start reaches the broker");
        var whileWaiting = gateway.ReportsSentSoFar();

        await executor.StopAsync(Guid.NewGuid(), run.RunId, CancellationToken.None);
        await ReportedItsEndAsync(gateway, run.RunId);

        whileWaiting.Should().Equal((OutOfDateRecord, CallReport.Received));
        gateway.ReportsSentSoFar().Should().Equal(
            (OutOfDateRecord, CallReport.Received), (OutOfDateRecord, CallReport.Failed("Run stopped: withdrawn by its owner")));
    }

    [Fact]
    public async Task A_run_stopped_while_its_node_is_called_reports_failed_saying_it_was_stopped()
    {
        var waiting = TestGraphs.WaitingPipeline();
        PipelineExecutor executor = null!;
        Task? stopping = null;
        var gateway = new FakeGateway(waiting.Fixture.Build());
        gateway.OnCall = (_, envelope) =>
        {
            stopping ??= executor.StopAsync(Guid.NewGuid(), envelope.GetProperty("runId").GetGuid(), CancellationToken.None);
            return Answer("notice", "sent for review");
        };
        executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());

        var result = await executor.RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await stopping!;
        await ReportedItsEndAsync(gateway, result.RunId);

        gateway.RunStatuses[result.RunId].Should().Be(RunStatus.Cancelled);
        gateway.ReportsSentSoFar().Last().Should().Be((OutOfDateRecord, CallReport.Failed("Run stopped: withdrawn by its owner")));
    }

    [Fact]
    public async Task A_run_started_any_other_way_reports_nothing_and_is_released_when_it_ends()
    {
        var (retry, gateway, executor) = Arrange(() => Answer());

        var byHand = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Catchment);
        var byAStateEntry = await executor.RunAsync(retry.PipelineId, Seed(), CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await ReportedItsEndAsync(gateway, byAStateEntry.RunId);

        gateway.ReportsSentSoFar().Should().OnlyContain(sent => sent.CallRecordId == OutOfDateRecord).And.HaveCount(2);
        gateway.CallRecordsOfRuns.Should().NotContainKey(byHand.RunId);
        gateway.HeldUntilReported.Should().NotContain(byHand.RunId);
    }

    [Fact]
    public async Task An_orchestrator_restarted_mid_run_reports_to_the_call_record_the_run_kept_when_the_run_ends()
    {
        var retry = TestGraphs.RetryPipeline(policy: [("attempts", 3), ("pauseMinutes", 1)]);
        var gateway = new FakeGateway(retry.Fixture.Build()) { OnCall = (_, _) => Answer() };
        var runId = Guid.NewGuid();
        var anHourAgo = new DateTimeOffset(2026, 11, 15, 11, 0, 0, TimeSpan.Zero);
        gateway.Runs[runId] = (retry.PipelineId, Seed());
        gateway.CallRecordsOfRuns[runId] = OutOfDateRecord;
        gateway.Records[(runId, retry.In.Id)] = new NodeRecord(RunStatus.Succeeded, new Dictionary<string, JsonElement> { ["seed"] = JsonSerializer.SerializeToElement("rainfall") });
        gateway.Records[(runId, retry.Echo.Id)] = new NodeRecord(RunStatus.Trying, new Dictionary<string, JsonElement>(),
            [new AttemptRecord(1, anHourAgo, anHourAgo.AddSeconds(1), RunStatus.Failed, "the rainfall service was away")]);
        var restarted = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, FastClock());

        (await restarted.RecoverUnfinishedAsync(CancellationToken.None)).Should().Be(1);
        await ReportedItsEndAsync(gateway, runId);

        gateway.ReportsSentSoFar().Should().Equal((OutOfDateRecord, CallReport.Completed));
        gateway.HeldUntilReported.Should().Contain(runId);
    }

    [Fact]
    public async Task An_orchestrator_restarted_while_a_run_waited_reports_its_end_once_the_answer_resumes_it()
    {
        var waiting = TestGraphs.WaitingPipeline();
        var gateway = new FakeGateway(waiting.Fixture.Build()) { OnCall = (subdomain, _) => subdomain == "review" ? Answer("notice", "sent for review") : Answer() };
        var run = await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock())
            .RunAsync(waiting.PipelineId, default, CancellationToken.None, subject: Catchment, callRecordId: OutOfDateRecord);
        await Settle.UntilAsync(() => gateway.ReportsSentSoFar().Count == 1, "the start reaches the broker");
        gateway.Answers[(run.RunId, waiting.Review.Id)] = WaitAnswer.Done;

        var recovered = await new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock()).RecoverWaitsAsync(CancellationToken.None);
        await ReportedItsEndAsync(gateway, run.RunId);

        recovered.Resumed.Should().Be(1);
        gateway.ReportsSentSoFar().Should().Equal((OutOfDateRecord, CallReport.Received), (OutOfDateRecord, CallReport.Completed));
    }

    [Fact]
    public async Task A_restarted_orchestrator_sends_the_reports_its_held_runs_owe()
    {
        var (_, gateway, executor) = Arrange(() => Answer());
        var ended = Guid.NewGuid();
        gateway.Owed.Add(new OwedReport(ended, OutOfDateRecord, RunStatus.Succeeded, null));

        (await executor.SendOwedReportsAsync(CancellationToken.None)).Should().Be(1);
        await ReportedItsEndAsync(gateway, ended);

        gateway.ReportsSentSoFar().Should().Equal((OutOfDateRecord, CallReport.Completed));
    }
}
