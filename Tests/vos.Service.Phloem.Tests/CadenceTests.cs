using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Phloem.Execution;
using vos.Service.Phloem.Model;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Phloem.Tests;

public class CadenceTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private static Cadence Hourly(DateTimeOffset? lastDue) => new(Guid.NewGuid(), "Every hour", 60, lastDue);

    [Fact]
    public void An_hourly_cadence_is_due_at_the_hour_after_its_last_and_not_before()
    {
        CadenceSchedule.DueAt(Hourly(Nine), Nine.AddMinutes(59), Nine).Should().BeEmpty();
        CadenceSchedule.DueAt(Hourly(Nine), Nine.AddMinutes(61), Nine.AddMinutes(59)).Should().Equal(Nine + Hour);
    }

    [Fact]
    public void A_cadence_never_run_or_last_due_later_than_the_clock_is_due_now()
    {
        var now = Nine.AddMinutes(17);

        CadenceSchedule.DueAt(Hourly(null), now, now).Should().Equal(now);
        CadenceSchedule.DueAt(Hourly(now.AddDays(1)), now, now).Should().Equal(now);
    }

    [Fact]
    public void A_cadence_with_no_interval_is_never_due()
    {
        CadenceSchedule.DueAt(new Cadence(Guid.NewGuid(), "Never", 0, null), Nine, Nine).Should().BeEmpty();
    }

    [Fact]
    public void Each_hour_the_clock_ran_through_since_the_last_look_is_due_by_itself()
    {
        CadenceSchedule.DueAt(Hourly(Nine), Nine.AddHours(3).AddMinutes(5), Nine.AddMinutes(1))
            .Should().Equal(Nine + Hour, Nine + 2 * Hour, Nine + 3 * Hour);
    }

    [Fact]
    public void Hours_the_clock_jumped_over_run_once_at_the_latest()
    {
        var aYearOn = Nine.AddDays(365).AddMinutes(30);

        CadenceSchedule.DueAt(Hourly(Nine), aYearOn, aYearOn.AddSeconds(-2)).Should().Equal(Nine.AddDays(365));
    }

    [Fact]
    public void Of_the_hours_due_only_those_the_clock_ran_through_run_one_each()
    {
        CadenceSchedule.DueAt(Hourly(Nine), Nine.AddHours(5).AddMinutes(10), Nine.AddHours(3).AddMinutes(30))
            .Should().Equal(Nine + 4 * Hour, Nine + 5 * Hour);
    }

    [Theory]
    [InlineData(1, 600, 30, 5)]
    [InlineData(300, 1, 60, 5)]
    [InlineData(36000, 1, 2, 20)]
    public async Task An_hourly_cadence_starts_one_run_per_model_hour_at_any_clock_speed(double rate, int realSecondsBetweenLooks, int looks, int hours)
    {
        var (fixture, gateway, keeper, real, _) = Arrange(rate, lastDue: Nine);

        await Task.WhenAll(await keeper.LookAsync(CancellationToken.None));
        for (var look = 0; look < looks; look++)
        {
            real.Advance(TimeSpan.FromSeconds(realSecondsBetweenLooks));
            await Task.WhenAll(await keeper.LookAsync(CancellationToken.None));
        }

        var dues = DuesOfRunsStarted(gateway);
        dues.Should().Equal(Enumerable.Range(1, hours).Select(hour => Nine + hour * Hour));
        gateway.RunsCreated.Should().OnlyContain(run => run.PipelineId == fixture.PipelineId && run.Subject == new RunSubject(fixture.CadenceId, "Every hour"));
        gateway.LastDueWritten.Last().Due.Should().Be(Nine + hours * Hour);
    }

    [Fact]
    public async Task A_clock_moved_a_year_ahead_starts_one_run_for_the_latest_hour()
    {
        var (_, gateway, keeper, real, clock) = Arrange(1, lastDue: Nine);
        await keeper.LookAsync(CancellationToken.None);

        clock.AnchorTo(Nine.AddDays(365).AddMinutes(30), 1);
        real.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(await keeper.LookAsync(CancellationToken.None));

        DuesOfRunsStarted(gateway).Should().Equal(Nine.AddDays(365));
    }

    [Fact]
    public async Task A_clock_sped_up_runs_every_hour_its_step_carried_it_through()
    {
        var (_, gateway, keeper, real, clock) = Arrange(1, lastDue: Nine);
        await keeper.LookAsync(CancellationToken.None);

        real.Advance(TimeSpan.FromSeconds(1));
        clock.AnchorTo(Nine + TimeSpan.FromSeconds(1) + 5 * Hour, 3600);
        await Task.WhenAll(await keeper.LookAsync(CancellationToken.None));

        DuesOfRunsStarted(gateway).Should().Equal(Enumerable.Range(1, 5).Select(hour => Nine + hour * Hour));
    }

    [Fact]
    public async Task An_orchestrator_starting_while_hours_are_due_runs_only_the_latest()
    {
        var (_, gateway, keeper, _, _) = Arrange(1, lastDue: Nine - 5 * Hour);

        await Task.WhenAll(await keeper.LookAsync(CancellationToken.None));

        DuesOfRunsStarted(gateway).Should().Equal(Nine);
    }

    [Fact]
    public async Task A_cadence_that_cannot_be_marked_due_starts_nothing()
    {
        var (_, gateway, keeper, _, _) = Arrange(1, lastDue: null);
        gateway.LastDueFailure = new HttpRequestException("the broker refused the write");

        var runs = await keeper.LookAsync(CancellationToken.None);

        runs.Should().BeEmpty();
        gateway.RunsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cadence_nothing_is_drawn_from_is_marked_due_and_starts_nothing()
    {
        var (fixture, gateway, keeper, _, _) = Arrange(1, lastDue: null);
        fixture.Fixture.Unrelate(fixture.Fixture.Get("Arrival"), fixture.Fixture.Get("standsFor"), fixture.Fixture.Get("Every hour"));
        gateway.Graph = fixture.Fixture.Build();

        var runs = await keeper.LookAsync(CancellationToken.None);

        runs.Should().BeEmpty();
        gateway.LastDueWritten.Should().ContainSingle();
    }

    [Fact]
    public void Looks_that_keep_failing_come_less_often_up_to_once_a_minute()
    {
        Enumerable.Range(0, 9).Select(CadenceKeeper.WaitBeforeLook).Select(wait => wait.TotalSeconds)
            .Should().Equal(1, 2, 4, 8, 16, 32, 60, 60, 60);
    }

    [Fact]
    public async Task Looks_that_keep_failing_are_reported_once_and_their_end_once()
    {
        var fixture = TestGraphs.CadencePipeline();
        var gateway = new FakeGateway(fixture.Fixture.Build()) { CadencesFailure = new HttpRequestException("the broker is away") };
        var logger = new RecordingLogger();
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, new ModelClock());
        var keeper = new CadenceKeeper(gateway, new PipelineStarter(gateway), executor, new ModelClock(), logger);

        var failures = 0;
        for (var look = 0; look < 3; look++) failures = await keeper.LookReportingFailuresAsync(failures, CancellationToken.None);
        var failedInARow = failures;
        gateway.CadencesFailure = null;
        failures = await keeper.LookReportingFailuresAsync(failures, CancellationToken.None);

        failedInARow.Should().Be(3);
        failures.Should().Be(0);
        logger.Entries.Should().Equal(LogLevel.Warning, LogLevel.Information);
    }

    private sealed class RecordingLogger : ILogger<CadenceKeeper>
    {
        public List<LogLevel> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(logLevel);
    }

    private static List<DateTimeOffset> DuesOfRunsStarted(FakeGateway gateway) =>
        gateway.Runs.Values.Select(run => DateTimeOffset.Parse(run.Parameters.GetProperty(ModelNames.Due).GetString()!)).Order().ToList();

    private static (TestGraphs.CadenceFixture Fixture, FakeGateway Gateway, CadenceKeeper Keeper, MovableClock Real, ModelClock Clock) Arrange(
        double rate, DateTimeOffset? lastDue)
    {
        var fixture = TestGraphs.CadencePipeline();
        var gateway = new FakeGateway(fixture.Fixture.Build());
        gateway.Cadences.Add(new Cadence(fixture.CadenceId, "Every hour", 60, lastDue));
        var real = new MovableClock(new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero));
        var clock = new ModelClock(real);
        clock.AnchorTo(Nine, rate);
        var executor = new PipelineExecutor(gateway, NullLogger<PipelineExecutor>.Instance, clock);
        var keeper = new CadenceKeeper(gateway, new PipelineStarter(gateway), executor, clock, NullLogger<CadenceKeeper>.Instance, real);
        return (fixture, gateway, keeper, real, clock);
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
