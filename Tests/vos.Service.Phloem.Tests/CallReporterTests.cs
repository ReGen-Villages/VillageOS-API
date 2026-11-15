using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Shared.ServiceCalls;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class CallReporterTests
{
    private static readonly Guid Run = Guid.NewGuid();
    private static readonly Guid CallRecord = Guid.NewGuid();

    private static (FakeGateway Gateway, CallReporter Reporter, List<TimeSpan> Pauses) Arrange()
    {
        var gateway = new FakeGateway(new GraphFixture().Build());
        var pauses = new List<TimeSpan>();
        var reporter = new CallReporter(gateway, NullLogger.Instance, length =>
        {
            lock (pauses) pauses.Add(length);
            return Task.CompletedTask;
        });
        return (gateway, reporter, pauses);
    }

    private static Func<Guid, CallReport, Task> NotReachingTheBroker(int times)
    {
        var failures = 0;
        return (_, _) => Interlocked.Increment(ref failures) <= times
            ? Task.FromException(new HttpRequestException("the broker is away"))
            : Task.CompletedTask;
    }

    [Fact]
    public async Task A_report_that_does_not_reach_the_broker_is_sent_again_after_each_pause_longer_than_the_last()
    {
        var (gateway, reporter, pauses) = Arrange();
        gateway.OnReport = NotReachingTheBroker(times: 3);

        await reporter.SendAsync(Run, CallRecord, CallReport.Completed);

        gateway.ReportsSentSoFar().Should().Equal(Enumerable.Repeat((CallRecord, CallReport.Completed), 4));
        pauses.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        gateway.Released.Should().Equal(Run);
    }

    [Fact]
    public void The_pause_doubles_until_it_reaches_five_minutes_and_grows_no_further()
    {
        Enumerable.Range(1, 12).Select(CallReporter.PauseAfter).Should().Equal(
            new[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 300, 300, 300 }.Select(seconds => TimeSpan.FromSeconds(seconds)));
        CallReporter.PauseAfter(10_000).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_broker_answer_that_the_record_no_longer_waits_stops_the_sending_and_releases_the_run()
    {
        var (gateway, reporter, pauses) = Arrange();
        gateway.OnReport = (record, report) => Task.FromException(new CallRecordNoLongerWaitingException(record, report, "already Completed"));

        await reporter.SendAsync(Run, CallRecord, CallReport.Failed("Run stopped: the catchment was withdrawn"));

        gateway.ReportsSentSoFar().Should().ContainSingle();
        pauses.Should().BeEmpty();
        gateway.Released.Should().Equal(Run);
    }

    [Fact]
    public async Task A_call_record_the_broker_no_longer_holds_stops_the_sending_and_releases_the_run()
    {
        var (gateway, reporter, pauses) = Arrange();
        gateway.OnReport = (_, _) => Task.FromException(new HttpRequestException("no such relationship", null, HttpStatusCode.NotFound));

        await reporter.SendAsync(Run, CallRecord, CallReport.Completed);

        gateway.ReportsSentSoFar().Should().ContainSingle();
        pauses.Should().BeEmpty();
        gateway.Released.Should().Equal(Run);
    }

    [Fact]
    public async Task A_report_the_broker_refuses_for_any_other_reason_is_not_sent_again()
    {
        var (gateway, reporter, pauses) = Arrange();
        gateway.OnReport = (_, _) => Task.FromException(new HttpRequestException("only the called service may report", null, HttpStatusCode.Forbidden));

        await reporter.SendAsync(Run, CallRecord, CallReport.Completed);

        gateway.ReportsSentSoFar().Should().ContainSingle();
        pauses.Should().BeEmpty();
    }

    [Fact]
    public async Task A_broker_that_fails_while_taking_a_report_is_sent_it_again()
    {
        var (gateway, reporter, _) = Arrange();
        var answered = 0;
        gateway.OnReport = (_, _) => Interlocked.Increment(ref answered) == 1
            ? Task.FromException(new HttpRequestException("the broker could not store it", null, HttpStatusCode.InternalServerError))
            : Task.CompletedTask;

        await reporter.SendAsync(Run, CallRecord, CallReport.Received);

        gateway.ReportsSentSoFar().Should().HaveCount(2);
    }

    [Fact]
    public async Task Only_a_report_that_finishes_the_call_releases_the_run()
    {
        var (gateway, reporter, _) = Arrange();

        await reporter.SendAsync(Run, CallRecord, CallReport.Received);

        gateway.Released.Should().BeEmpty();
    }

    [Fact]
    public async Task A_run_end_is_sent_only_once_its_start_has_reached_the_broker()
    {
        var (gateway, reporter, _) = Arrange();
        var startTaken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.OnReport = (_, report) => report == CallReport.Received ? startTaken.Task : Task.CompletedTask;

        var start = reporter.SendAsync(Run, CallRecord, CallReport.Received);
        var end = reporter.SendAsync(Run, CallRecord, CallReport.Completed);
        await Settle.UntilAsync(() => gateway.ReportsSentSoFar().Count == 1, "the start is sent");
        var sentBeforeTheStartWasTaken = gateway.ReportsSentSoFar();
        startTaken.SetResult();
        await Task.WhenAll(start, end);

        sentBeforeTheStartWasTaken.Should().Equal((CallRecord, CallReport.Received));
        gateway.ReportsSentSoFar().Select(sent => sent.Report).Should().Equal(CallReport.Received, CallReport.Completed);
    }

    public static TheoryData<string, string?, string, string?> EachStatusAndItsReport() => new()
    {
        { RunStatus.Running, null, "received", null },
        { RunStatus.Waiting, null, "received", null },
        { RunStatus.Trying, null, "received", null },
        { RunStatus.Succeeded, null, "completed", null },
        { RunStatus.Failed, "'Estimate rainfall' failed, and no fail wire leaves it.", "failed", "'Estimate rainfall' failed, and no fail wire leaves it." },
        { RunStatus.Cancelled, "the catchment was withdrawn", "failed", "Run stopped: the catchment was withdrawn" },
        { RunStatus.Refused, "Pipeline is invalid: it has no Output node.", "failed", "Pipeline is invalid: it has no Output node." },
        { RunStatus.Failed, null, "failed", "The run ended failed, and its record gives no reason." },
    };

    [Theory]
    [MemberData(nameof(EachStatusAndItsReport))]
    public void A_runs_status_names_the_report_it_owes(string status, string? error, string word, string? reason)
    {
        var report = CallReporter.ReportFor(status, error);

        report.Word.Should().Be(word);
        report.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task A_restarted_orchestrator_sends_every_report_its_held_runs_still_owe()
    {
        var (gateway, reporter, _) = Arrange();
        var running = (Run: Guid.NewGuid(), CallRecord: Guid.NewGuid());
        var ended = (Run: Guid.NewGuid(), CallRecord: Guid.NewGuid());
        gateway.Owed.Add(new OwedReport(running.Run, running.CallRecord, RunStatus.Running, null));
        gateway.Owed.Add(new OwedReport(ended.Run, ended.CallRecord, RunStatus.Cancelled, "the catchment was withdrawn"));

        var owed = await reporter.SendOwedAsync(CancellationToken.None);

        owed.Should().Be(2);
        await Settle.UntilAsync(() => gateway.ReportsSentSoFar().Count == 2 && gateway.Released.Count == 1, "both reports are sent");
        gateway.ReportsSentSoFar().Should().BeEquivalentTo(new[]
        {
            (running.CallRecord, CallReport.Received),
            (ended.CallRecord, CallReport.Failed("Run stopped: the catchment was withdrawn")),
        });
        gateway.Released.Should().Equal(ended.Run);
    }
}
