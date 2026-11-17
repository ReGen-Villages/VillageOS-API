using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using vos.Service.Phloem.Execution;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class RunClearingTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly KeepPeriods Template = new(PassedDays: 30, FailedDays: 90);

    private static RunToJudge Run(string status, double daysAgo, bool held = false, Guid? pipeline = null) =>
        new(Guid.NewGuid(), pipeline, status, Now.AddDays(-daysAgo), held);

    [Theory]
    [InlineData(RunStatus.Succeeded, 31, true)]
    [InlineData(RunStatus.Succeeded, 29, false)]
    [InlineData(RunStatus.Cancelled, 31, true)]
    [InlineData(RunStatus.Failed, 31, false)]
    [InlineData(RunStatus.Failed, 91, true)]
    [InlineData(RunStatus.Refused, 91, true)]
    [InlineData(RunStatus.Refused, 31, false)]
    public void A_finished_run_is_due_once_older_than_the_period_for_how_it_ended(string status, double daysAgo, bool due) =>
        RunRetention.IsDue(Run(status, daysAgo), Template, null, Now).Should().Be(due);

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Waiting)]
    [InlineData(RunStatus.Trying)]
    public void A_run_still_running_waiting_or_trying_is_never_due_however_old(string status) =>
        RunRetention.IsDue(Run(status, 10_000), Template, null, Now).Should().BeFalse();

    [Fact]
    public void A_run_the_orchestrator_still_holds_is_never_due() =>
        RunRetention.IsDue(Run(RunStatus.Succeeded, 400, held: true), Template, null, Now).Should().BeFalse();

    [Fact]
    public void A_run_with_no_finish_recorded_is_never_due() =>
        RunRetention.IsDue(Run(RunStatus.Succeeded, 400) with { FinishedAt = null }, Template, null, Now).Should().BeFalse();

    [Fact]
    public void A_pipelines_own_period_wins_over_the_kinds_for_each_way_a_run_ends()
    {
        var pipeline = new KeepPeriods(PassedDays: 7, FailedDays: null);

        RunRetention.IsDue(Run(RunStatus.Succeeded, 8), Template, pipeline, Now).Should().BeTrue();
        RunRetention.IsDue(Run(RunStatus.Failed, 31), Template, pipeline, Now).Should().BeFalse("its failed period falls back to the kind's");
    }

    [Fact]
    public void A_model_stating_no_period_keeps_every_run() =>
        RunRetention.IsDue(Run(RunStatus.Succeeded, 10_000), new KeepPeriods(null, null), null, Now).Should().BeFalse();

    [Fact]
    public void A_stop_request_goes_with_the_last_run_it_names()
    {
        var run = Guid.NewGuid();
        var request = Guid.NewGuid();

        RunRetention.StopRequestsGoingWith(run, new Dictionary<Guid, IReadOnlySet<Guid>> { [request] = new HashSet<Guid> { run } })
            .Should().Equal(request);
    }

    [Fact]
    public void A_stop_request_stays_while_another_run_it_names_is_kept()
    {
        var run = Guid.NewGuid();
        var kept = Guid.NewGuid();

        RunRetention.StopRequestsGoingWith(run, new Dictionary<Guid, IReadOnlySet<Guid>> { [Guid.NewGuid()] = new HashSet<Guid> { run, kept } })
            .Should().BeEmpty();
    }

    [Fact]
    public void A_stop_that_ended_many_runs_of_one_subject_goes_once_after_the_last_of_them()
    {
        var requester = Guid.NewGuid();
        var stopped = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var request = Guid.NewGuid();
        var named = new Dictionary<Guid, HashSet<Guid>> { [request] = [requester, .. stopped] };
        var clearedWith = new List<(Guid Run, Guid Request)>();

        foreach (var run in stopped.Prepend(requester))
        {
            var linked = named.Where(entry => entry.Value.Contains(run)).ToDictionary(entry => entry.Key, entry => (IReadOnlySet<Guid>)entry.Value);
            foreach (var going in RunRetention.StopRequestsGoingWith(run, linked))
            {
                clearedWith.Add((run, going));
                named.Remove(going);
            }
            foreach (var runs in named.Values) runs.Remove(run);
        }

        clearedWith.Should().Equal((stopped[^1], request));
    }

    [Fact]
    public async Task One_look_clears_the_runs_past_their_period_and_leaves_one_that_cannot_be_cleared_for_the_next()
    {
        var old = Run(RunStatus.Succeeded, 31);
        var refusing = Run(RunStatus.Succeeded, 40);
        var recent = Run(RunStatus.Succeeded, 1);
        var running = Run(RunStatus.Running, 100);
        var gateway = new FakeGateway(new GraphFixture().Build())
        {
            RunsJudged = new RunsToJudge([old, refusing, recent, running], Template, new Dictionary<Guid, KeepPeriods>()),
        };
        gateway.RefusesToClear.Add(refusing.RunId);
        var clock = new ModelClock(new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 23, 0, 0, TimeSpan.Zero)));
        clock.AnchorTo(Now, 1);

        var cleared = await new RunClearingKeeper(gateway, clock, NullLogger<RunClearingKeeper>.Instance).ClearOnceAsync(CancellationToken.None);

        cleared.Should().Be((1, 3));
        gateway.Cleared.Should().Equal(old.RunId);
    }
}
