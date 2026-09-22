using System.Globalization;
using Microsoft.Extensions.Logging;
using vos.Service.Forage.Helpers;

namespace vos.Service.Forage.Services;

// A source addressed with a window of dates is fetched a slice of that window at a time.
//
// A source declaring a reach is asked for every year of it in one call, and everything that follows
// carries the whole of it: the provider's answer, the reshaped readings, and one write holding every
// reading. At the shipped reach of a decade of hourly readings that is millions of readings in one
// answer — enough to exhaust the fetching service while it reshapes, and to be refused as one write.
// A slice bounds all three, and the run's own timeout then bounds each slice rather than the decade.
//
// Slicing is decided here rather than by the model, because how much a service can hold in one pass
// is a fact about this deployment, while the reach is a fact about what the site's analysis needs.
public sealed class SlicedWindowFetcher : ISourceFetcher
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly ISourceFetcher _inner;
    private readonly int _daysPerSlice;
    private readonly ILogger<SlicedWindowFetcher> _logger;

    public SlicedWindowFetcher(ISourceFetcher inner, int daysPerSlice, ILogger<SlicedWindowFetcher> logger)
    {
        if (daysPerSlice <= 0)
            throw new ArgumentOutOfRangeException(nameof(daysPerSlice), "A slice covers at least one day");

        _inner = inner;
        _daysPerSlice = daysPerSlice;
        _logger = logger;
    }

    public async Task<SourceOutcome> FetchAsync(
        Guid subjectId,
        string sourceName,
        string endpointName,
        IReadOnlyDictionary<string, string> addressParameters,
        CancellationToken cancellationToken)
    {
        if (Slices(addressParameters) is not { Count: > 1 } slices)
            return await _inner.FetchAsync(subjectId, sourceName, endpointName, addressParameters, cancellationToken);

        _logger.LogInformation(
            "Source {Source} reaches back {Days} day(s) for {Subject}: fetching it in {Slices} slices",
            sourceName, Span(addressParameters), subjectId, slices.Count);

        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (start, end) in slices)
        {
            var outcome = await _inner.FetchAsync(
                subjectId, sourceName, endpointName, With(addressParameters, start, end), cancellationToken);

            // The first slice that fails ends the fetch: the rest would ask the same provider for the
            // same reason to refuse, and a source half written is reported unresolved either way.
            if (!outcome.Resolved)
                return outcome with
                {
                    Reason = $"{start.ToString(DateFormat, CultureInfo.InvariantCulture)} to "
                             + $"{end.ToString(DateFormat, CultureInfo.InvariantCulture)}: {outcome.Reason}",
                };

            foreach (var (name, value) in outcome.Written ?? new Dictionary<string, string>())
                written[name] = value;
        }

        return new SourceOutcome(sourceName, true, null, SubjectId: subjectId, Written: written);
    }

    // Null where the call carries no window, or one this deployment can take in a single pass.
    private List<(DateOnly Start, DateOnly End)>? Slices(IReadOnlyDictionary<string, string> addressParameters)
    {
        if (!TryReadWindow(addressParameters, out var start, out var end))
            return null;

        var slices = new List<(DateOnly, DateOnly)>();
        var from = start;
        while (from <= end)
        {
            // Each slice ends the day before the next begins, so a reading at a boundary belongs to
            // exactly one of them however the provider treats an inclusive end.
            var to = from.AddDays(_daysPerSlice - 1);
            slices.Add((from, to > end ? end : to));
            from = to.AddDays(1);
        }
        return slices;
    }

    private static int Span(IReadOnlyDictionary<string, string> addressParameters) =>
        TryReadWindow(addressParameters, out var start, out var end) ? end.DayNumber - start.DayNumber + 1 : 0;

    private static bool TryReadWindow(
        IReadOnlyDictionary<string, string> addressParameters, out DateOnly start, out DateOnly end)
    {
        start = default;
        end = default;
        return addressParameters.TryGetValue(CoveringSourceResolver.WindowStartPlaceholder, out var from)
               && addressParameters.TryGetValue(CoveringSourceResolver.WindowEndPlaceholder, out var to)
               && DateOnly.TryParseExact(from, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out start)
               && DateOnly.TryParseExact(to, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out end)
               && start <= end;
    }

    private static Dictionary<string, string> With(
        IReadOnlyDictionary<string, string> addressParameters, DateOnly start, DateOnly end) =>
        new(addressParameters, StringComparer.OrdinalIgnoreCase)
        {
            [CoveringSourceResolver.WindowStartPlaceholder] = start.ToString(DateFormat, CultureInfo.InvariantCulture),
            [CoveringSourceResolver.WindowEndPlaceholder] = end.ToString(DateFormat, CultureInfo.InvariantCulture),
        };
}
