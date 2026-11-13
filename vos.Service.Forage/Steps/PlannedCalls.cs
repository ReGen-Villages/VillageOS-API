using System.Text.Json;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;

namespace vos.Service.Forage.Steps;

// One call as Plan calls hands it out and Tributary hands it back: the three fields Tributary reads, and what Record
// results needs to find the record the answer goes on.
public sealed record PlannedCall(
    string EndpointName,
    Guid SubjectId,
    IReadOnlyDictionary<string, string> AddressParameters,
    Guid SourceId,
    string Source,
    string Subject,
    Guid? RecordId,
    long Attempts,
    CallSlice? Slice = null);

public sealed record CallSlice(int Index, int Count);

// What one call came to, all its slices together. Reason is null for a call that resolved.
public sealed record CallResult(
    Guid SubjectId,
    Guid SourceId,
    string Source,
    string Subject,
    IReadOnlyDictionary<string, string>? Written,
    string? Reason)
{
    public bool Resolved => Reason is null;
}

// Reason is null for an item that resolved.
public sealed record ItemOutcome(PlannedCall Call, IReadOnlyDictionary<string, string>? Written, string? Reason);

public static class PlannedCalls
{
    // One item per call, or one per slice where the call asks for a long history.
    public static IReadOnlyList<PlannedCall> From(IReadOnlyList<OutstandingCall> outstanding, int daysPerSlice) =>
    [
        .. outstanding.SelectMany(call =>
        {
            var planned = new PlannedCall(call.Source.EndpointName, call.Call.SubjectId, call.Call.Values,
                call.Source.SourceId, call.Source.Name, call.Call.SubjectName, call.CoverageId, call.Attempts);
            return WindowSlices.Of(call.Call.Values, daysPerSlice) is not { } slices
                ? [planned]
                : slices.Select((slice, index) => planned with
                {
                    AddressParameters = WindowSlices.With(call.Call.Values, slice.Start, slice.End),
                    Slice = new CallSlice(index, slices.Count),
                });
        }),
    ];

    // A call resolved only when every slice of it did, with what each slice wrote taken together in slice order.
    // A call with a failed slice keeps the first failed slice's reason, naming the dates it asked for.
    public static IReadOnlyList<(PlannedCall Call, CallResult Result)> Fold(IEnumerable<ItemOutcome> outcomes) =>
    [
        .. outcomes
            .GroupBy(outcome => (outcome.Call.SubjectId, outcome.Call.SourceId))
            .Select(call =>
            {
                var slices = call.OrderBy(outcome => outcome.Call.Slice?.Index ?? 0).ToList();
                var first = slices[0].Call;
                var count = first.Slice?.Count ?? 1;
                var reason = ReasonFor(slices, count);
                var written = reason is null
                    ? slices.SelectMany(slice => slice.Written ?? new Dictionary<string, string>())
                        .GroupBy(value => value.Key)
                        .ToDictionary(value => value.Key, value => value.Last().Value, StringComparer.Ordinal)
                    : null;
                return (first, new CallResult(first.SubjectId, first.SourceId, first.Source, first.Subject, written, reason));
            }),
    ];

    private static string? ReasonFor(IReadOnlyList<ItemOutcome> slices, int count)
    {
        if (slices.FirstOrDefault(slice => slice.Reason is not null) is { } failed)
            return failed.Call.Slice is null ? failed.Reason : $"{WindowOf(failed.Call)}: {failed.Reason}";

        var answered = slices.Select(slice => slice.Call.Slice?.Index ?? 0).Distinct().Count();
        return answered == count ? null : $"{count - answered} of {count} slices came back with no answer.";
    }

    private static string WindowOf(PlannedCall call) =>
        $"{call.AddressParameters.GetValueOrDefault(CoveringSourceResolver.WindowStartPlaceholder)} to "
        + call.AddressParameters.GetValueOrDefault(CoveringSourceResolver.WindowEndPlaceholder);

    // An outcome as Tributary's step answers it: the call it was handed, and what it wrote or why it failed. Only
    // text can name a vocabulary member, so only text is kept of what was written.
    public static ItemOutcome OutcomeOf(JsonElement outcome, bool resolved)
    {
        if (outcome.ValueKind != JsonValueKind.Object
            || !outcome.TryGetProperty("call", out var call)
            || call.Deserialize<PlannedCall>(JsonSerializerOptions.Web) is not { } planned)
            throw new InvalidOperationException("An outcome reaching Record results carries no call it can match to its record.");

        var written = outcome.TryGetProperty("written", out var values) && values.ValueKind == JsonValueKind.Object
            ? values.EnumerateObject()
                .Where(value => value.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal)
            : null;
        if (resolved) return new ItemOutcome(planned, written, null);

        var reason = outcome.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null;
        return new ItemOutcome(planned, null, reason ?? "The call failed and its answer gave no reason.");
    }
}
