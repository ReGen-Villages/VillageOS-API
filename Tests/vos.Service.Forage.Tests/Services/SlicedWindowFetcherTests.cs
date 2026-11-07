using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Configuration;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

public class SlicedWindowFetcherTests
{
    private const string Source = "Open-Meteo climate history";
    private const string Endpoint = "climate-history";
    private static readonly Guid Site = Guid.NewGuid();

    private sealed class RecordingFetcher : ISourceFetcher
    {
        private readonly Func<int, SourceOutcome> _answer;

        public RecordingFetcher(Func<int, SourceOutcome>? answer = null) =>
            _answer = answer ?? (_ => new SourceOutcome(Source, true, null));

        public List<IReadOnlyDictionary<string, string>> Calls { get; } = [];

        public Task<SourceOutcome> FetchAsync(
            Guid subjectId, string sourceName, string endpointName,
            IReadOnlyDictionary<string, string> addressParameters, CancellationToken cancellationToken)
        {
            Calls.Add(new Dictionary<string, string>(addressParameters, StringComparer.OrdinalIgnoreCase));
            return Task.FromResult(_answer(Calls.Count));
        }
    }

    private static Dictionary<string, string> Window(string from, string to) => new(StringComparer.OrdinalIgnoreCase)
    {
        [CoveringSourceResolver.WindowStartPlaceholder] = from,
        [CoveringSourceResolver.WindowEndPlaceholder] = to,
        ["latitude"] = "52.52",
    };

    private static SlicedWindowFetcher Slicing(
        ISourceFetcher inner,
        int daysPerSlice = 365,
        TimeProvider? clock = null,
        int attemptsPerSlice = ForageLaunchSettings.DefaultAttemptsPerSlice,
        ILogger<SlicedWindowFetcher>? logger = null) =>
        new(inner, daysPerSlice, attemptsPerSlice, clock ?? new ClockThatWaitsNoTime(),
            logger ?? NullLogger<SlicedWindowFetcher>.Instance);

    private static SourceOutcome TooManyRequests(TimeSpan wait) =>
        new(Source, false, "429: Minutely API request limit exceeded.", RetryAfter: wait);

    private static SourceOutcome TimedOut() =>
        new(Source, false, """504: {"error":"Service timeout"}""", TimedOut: true);

    private static SourceOutcome Answered(int call) =>
        new(Source, true, null, Written: new Dictionary<string, string> { [$"call{call}"] = "written" });

    private sealed class LevelledLogger : ILogger<SlicedWindowFetcher>
    {
        public List<(LogLevel Level, string Line)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));

        public List<(LogLevel Level, string Line)> AskedAgainAfterATimeout() =>
            Lines.Where(entry => entry.Line.StartsWith(SlicedWindowFetcher.TimedOutSliceAskedAgain, StringComparison.Ordinal))
                .ToList();
    }

    private static (DateOnly Start, DateOnly End) WindowOf(IReadOnlyDictionary<string, string> call) =>
        (DateOnly.Parse(call[CoveringSourceResolver.WindowStartPlaceholder]),
         DateOnly.Parse(call[CoveringSourceResolver.WindowEndPlaceholder]));

    [Fact]
    public async Task A_call_carrying_no_window_is_passed_straight_through()
    {
        var inner = new RecordingFetcher();

        await Slicing(inner).FetchAsync(
            Site, Source, Endpoint, new Dictionary<string, string> { ["latitude"] = "52.52" }, default);

        inner.Calls.Should().ContainSingle();
        inner.Calls[0].Should().NotContainKey(CoveringSourceResolver.WindowStartPlaceholder);
    }

    [Fact]
    public async Task A_window_this_deployment_can_take_in_one_pass_is_asked_for_once()
    {
        var inner = new RecordingFetcher();

        await Slicing(inner).FetchAsync(Site, Source, Endpoint, Window("2025-09-23", "2026-09-22"), default);

        inner.Calls.Should().ContainSingle();
        WindowOf(inner.Calls[0]).Should().Be((new DateOnly(2025, 9, 23), new DateOnly(2026, 9, 22)));
    }

    [Fact]
    public async Task A_decade_is_asked_for_a_year_at_a_time_covering_it_without_gap_or_overlap()
    {
        var inner = new RecordingFetcher();

        var outcome = await Slicing(inner).FetchAsync(
            Site, Source, Endpoint, Window("2016-09-23", "2026-09-22"), default);

        outcome.Resolved.Should().BeTrue();
        inner.Calls.Should().HaveCount(11, "ten years and a day is eleven slices of 365 days");

        var windows = inner.Calls.Select(WindowOf).ToList();
        windows[0].Start.Should().Be(new DateOnly(2016, 9, 23));
        windows[^1].End.Should().Be(new DateOnly(2026, 9, 22), "the last slice stops where the reach does");
        foreach (var (earlier, later) in windows.Zip(windows.Skip(1)))
            later.Start.Should().Be(earlier.End.AddDays(1), "each slice begins the day the one before it ended");
    }

    [Fact]
    public async Task Everything_but_the_window_is_carried_into_every_slice()
    {
        var inner = new RecordingFetcher();

        await Slicing(inner).FetchAsync(Site, Source, Endpoint, Window("2020-01-01", "2026-01-01"), default);

        inner.Calls.Should().OnlyContain(call => call["latitude"] == "52.52");
    }

    [Fact]
    public async Task A_slice_that_fails_ends_the_fetch_and_names_the_window_it_failed_on()
    {
        var inner = new RecordingFetcher(call => call == 3
            ? new SourceOutcome(Source, false, "503 from the provider")
            : new SourceOutcome(Source, true, null));

        var outcome = await Slicing(inner).FetchAsync(
            Site, Source, Endpoint, Window("2016-09-23", "2026-09-22"), default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("503 from the provider").And.Contain("2018-09-23");
        inner.Calls.Should().HaveCount(3, "the slices after the one that failed are not asked for");
    }

    [Fact]
    public async Task A_slice_refused_for_too_many_requests_is_asked_for_again_after_the_wait_and_every_slice_is_written()
    {
        var clock = new ClockThatWaitsNoTime();
        var inner = new RecordingFetcher(call => call == 11
            ? TooManyRequests(TimeSpan.FromSeconds(40))
            : new SourceOutcome(Source, true, null,
                Written: new Dictionary<string, string> { [$"call{call}"] = "written" }));

        var outcome = await Slicing(inner, clock: clock).FetchAsync(
            Site, Source, Endpoint, Window("2016-09-23", "2026-09-22"), default);

        outcome.Resolved.Should().BeTrue();
        inner.Calls.Should().HaveCount(12, "the eleventh slice is asked for twice");
        WindowOf(inner.Calls[11]).Should().Be(WindowOf(inner.Calls[10]), "the refused slice is the one asked for again");
        outcome.Written.Should().HaveCount(11, "every slice that answered is reported");
        clock.Waits.Should().Equal([TimeSpan.FromSeconds(40)], "the wait is the one the refusal asked for");
    }

    [Fact]
    public async Task A_slice_refused_for_too_many_requests_every_time_leaves_the_source_unresolved_with_the_providers_reason()
    {
        var clock = new ClockThatWaitsNoTime();
        var inner = new RecordingFetcher(_ => TooManyRequests(TimeSpan.FromMinutes(1)));

        var outcome = await Slicing(inner, clock: clock, attemptsPerSlice: 4).FetchAsync(
            Site, Source, Endpoint, Window("2016-09-23", "2026-09-22"), default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("Minutely API request limit exceeded.").And.Contain("2016-09-23");
        inner.Calls.Should().HaveCount(4);
        clock.Waits.Should().HaveCount(3, "nothing is waited for after the last attempt");
    }

    [Fact]
    public async Task A_slice_that_timed_out_once_is_asked_for_again_at_once_and_the_slices_after_it_are_fetched()
    {
        var clock = new ClockThatWaitsNoTime();
        var logger = new LevelledLogger();
        var inner = new RecordingFetcher(call => call == 2 ? TimedOut() : Answered(call));

        var outcome = await Slicing(inner, daysPerSlice: 10, clock: clock, logger: logger).FetchAsync(
            Site, Source, Endpoint, Window("2026-01-01", "2026-01-30"), default);

        outcome.Resolved.Should().BeTrue();
        inner.Calls.Should().HaveCount(4, "three slices, the second asked for twice");
        WindowOf(inner.Calls[2]).Should().Be(WindowOf(inner.Calls[1]), "the slice that timed out is the one asked for again");
        WindowOf(inner.Calls[3]).Should().Be((new DateOnly(2026, 1, 21), new DateOnly(2026, 1, 30)));
        outcome.Written.Should().HaveCount(3, "every slice that answered is reported");
        clock.Waits.Should().BeEmpty("a timeout names no wait");
        logger.AskedAgainAfterATimeout().Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Information);
        logger.AskedAgainAfterATimeout()[0].Line.Should().Contain("2026-01-11").And.Contain("2026-01-20");
    }

    [Fact]
    public async Task A_slice_that_times_out_every_time_is_asked_for_as_often_as_the_limit_allows_and_leaves_the_source_unresolved()
    {
        var clock = new ClockThatWaitsNoTime();
        var logger = new LevelledLogger();
        var inner = new RecordingFetcher(_ => TimedOut());

        var outcome = await Slicing(inner, daysPerSlice: 10, clock: clock, attemptsPerSlice: 5, logger: logger)
            .FetchAsync(Site, Source, Endpoint, Window("2026-01-01", "2026-01-30"), default);

        outcome.Resolved.Should().BeFalse();
        outcome.Reason.Should().Contain("Service timeout").And.Contain("2026-01-01 to 2026-01-10");
        inner.Calls.Should().HaveCount(5);
        inner.Calls.Select(WindowOf).Distinct().Should().ContainSingle("the slices after the one that failed are not asked for");
        logger.AskedAgainAfterATimeout().Should().HaveCount(4, "nothing is asked again after the last attempt");
        clock.Waits.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeouts_and_refusals_for_too_many_requests_at_one_slice_count_against_one_limit()
    {
        var clock = new ClockThatWaitsNoTime();
        var inner = new RecordingFetcher(call => call == 2 ? TooManyRequests(TimeSpan.FromSeconds(40)) : TimedOut());

        var outcome = await Slicing(inner, daysPerSlice: 10, clock: clock, attemptsPerSlice: 3)
            .FetchAsync(Site, Source, Endpoint, Window("2026-01-01", "2026-01-30"), default);

        outcome.Resolved.Should().BeFalse();
        inner.Calls.Should().HaveCount(3);
        clock.Waits.Should().Equal([TimeSpan.FromSeconds(40)]);
    }

    [Fact]
    public async Task A_refusal_asking_for_a_longer_wait_than_a_run_holds_a_slice_ends_the_fetch_at_once()
    {
        var clock = new ClockThatWaitsNoTime();
        var inner = new RecordingFetcher(
            _ => TooManyRequests(SlicedWindowFetcher.LongestWaitForARefusedSlice + TimeSpan.FromSeconds(1)));

        var outcome = await Slicing(inner, clock: clock).FetchAsync(
            Site, Source, Endpoint, Window("2016-09-23", "2026-09-22"), default);

        outcome.Resolved.Should().BeFalse();
        inner.Calls.Should().ContainSingle();
        clock.Waits.Should().BeEmpty();
    }

    private sealed class ClockThatNeverFires : TimeProvider
    {
        public TaskCompletionSource Waiting { get; } = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Waiting.TrySetResult();
            return new NeverFires();
        }

        private sealed class NeverFires : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task A_run_cancelled_while_a_slice_waits_stops_waiting_and_asks_nothing_more()
    {
        var clock = new ClockThatNeverFires();
        var inner = new RecordingFetcher(_ => TooManyRequests(TimeSpan.FromMinutes(1)));
        using var run = new CancellationTokenSource();

        var fetching = Slicing(inner, clock: clock).FetchAsync(
            Site, Source, Endpoint, Window("2016-09-23", "2026-09-22"), run.Token);
        await clock.Waiting.Task;
        await run.CancelAsync();

        await FluentActions.Awaiting(() => fetching.WaitAsync(Settle.Ceiling))
            .Should().ThrowAsync<OperationCanceledException>();
        inner.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task A_refusal_answered_in_time_and_naming_no_wait_is_not_asked_for_again()
    {
        var clock = new ClockThatWaitsNoTime();
        var inner = new RecordingFetcher(_ => new SourceOutcome(Source, false, "503 from the provider"));

        await Slicing(inner, clock: clock).FetchAsync(
            Site, Source, Endpoint, Window("2016-09-23", "2026-09-22"), default);

        inner.Calls.Should().ContainSingle();
        clock.Waits.Should().BeEmpty();
    }

    [Fact]
    public async Task What_each_slice_wrote_is_reported_together()
    {
        var inner = new RecordingFetcher(call => new SourceOutcome(
            Source, true, null,
            Written: new Dictionary<string, string> { [$"slice{call}"] = "written" }));

        var outcome = await Slicing(inner).FetchAsync(
            Site, Source, Endpoint, Window("2023-09-23", "2026-09-22"), default);

        outcome.Written.Should().HaveCount(inner.Calls.Count);
        outcome.SubjectId.Should().Be(Site);
    }

    [Fact]
    public void A_slice_covering_no_day_is_refused()
    {
        var refused = () => Slicing(new RecordingFetcher(), daysPerSlice: 0);

        refused.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_slice_allowed_no_attempt_is_refused()
    {
        var refused = () => Slicing(new RecordingFetcher(), attemptsPerSlice: 0);

        refused.Should().Throw<ArgumentOutOfRangeException>();
    }
}
