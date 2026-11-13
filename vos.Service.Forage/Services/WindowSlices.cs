using System.Globalization;
using vos.Service.Forage.Helpers;

namespace vos.Service.Forage.Services;

// A call addressed with a window of dates, cut into slices of a number of days.
public static class WindowSlices
{
    public const string DateFormat = "yyyy-MM-dd";

    // Null where the call carries no window, or one a single slice covers.
    public static List<(DateOnly Start, DateOnly End)>? Of(IReadOnlyDictionary<string, string> addressParameters, int daysPerSlice)
    {
        if (!TryReadWindow(addressParameters, out var start, out var end))
            return null;

        var slices = new List<(DateOnly, DateOnly)>();
        var from = start;
        while (from <= end)
        {
            // Each slice ends the day before the next begins, so a reading at a boundary belongs to
            // exactly one of them however the provider treats an inclusive end.
            var to = from.AddDays(daysPerSlice - 1);
            slices.Add((from, to > end ? end : to));
            from = to.AddDays(1);
        }
        return slices.Count > 1 ? slices : null;
    }

    public static Dictionary<string, string> With(
        IReadOnlyDictionary<string, string> addressParameters, DateOnly start, DateOnly end) =>
        new(addressParameters, StringComparer.OrdinalIgnoreCase)
        {
            [CoveringSourceResolver.WindowStartPlaceholder] = Text(start),
            [CoveringSourceResolver.WindowEndPlaceholder] = Text(end),
        };

    public static string Text(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

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
}
