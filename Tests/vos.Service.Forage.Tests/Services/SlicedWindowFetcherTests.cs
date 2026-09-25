using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

// A source reaching back years is asked for a slice at a time: the provider's answer, the reshape
// that follows it and the write it becomes all carry a whole window, and the shipped ten-year reach
// made each of those millions of readings — the fetching service ran out of memory reshaping one and
// the broker refused the write.
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

    private static SlicedWindowFetcher Slicing(ISourceFetcher inner, int daysPerSlice = 365) =>
        new(inner, daysPerSlice, NullLogger<SlicedWindowFetcher>.Instance);

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
}
