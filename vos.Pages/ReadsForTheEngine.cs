using System.Text.Json;

namespace vos.Pages;

/// <summary>
/// The object the bundle asks its reads of. The bundle names the read and gives its arguments as a JSON
/// list, and gets the answer back as JSON through a promise.
/// </summary>
public sealed class ReadsForTheEngine(IPageReads reads, CancellationToken cancellation)
{
    private const string NoAnswer = "null";

    // Lower case because JavaScript calls it by this name.
    public async Task<string> read(string method, string argumentsJson)
    {
        using var given = JsonDocument.Parse(argumentsJson);
        var arguments = given.RootElement;
        return method switch
        {
            "thingsInState" => await reads.ThingsInStateAsync(arguments[0].GetString()!, JsonOrNull(arguments[1]), cancellation),
            "thingRanges" => await reads.ThingRangesAsync(arguments[0].GetString()!, cancellation) ?? NoAnswer,
            "aggregate" => await reads.AggregateAsync(arguments[0].GetRawText(), cancellation),
            "reduce" => await reads.ReduceAsync(arguments[0].GetRawText(), cancellation),
            "fromService" => await reads.FromServiceAsync(arguments[0].GetString()!, JsonOrNull(arguments[1]), cancellation),
            _ => throw new InvalidOperationException($"The reader asked for a read this library does not know: {method}."),
        };
    }

    private static string? JsonOrNull(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetRawText();
}
