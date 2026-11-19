using System.Text.Json;

namespace vos.Pages.Tests;

/// <summary>
/// What a running Mycelium answered for the shared test seed, read back from the recording: the Things
/// each selector reached, the answer of each read a page made, and each page as the reader resolved it
/// in Node. A question the recording holds no answer for fails the test that asked it.
/// </summary>
internal sealed class RecordedModel : IPageReads
{
    private readonly JsonElement _recording;

    private RecordedModel(JsonElement recording) => _recording = recording;

    public static RecordedModel OfTheCatchmentsSeed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Recorded", "catchments.recorded.json");
        return new RecordedModel(JsonDocument.Parse(File.ReadAllText(path)).RootElement);
    }

    public IEnumerable<RecordedPage> Pages => _recording.GetProperty("pages").EnumerateArray().Select(page => new RecordedPage(page));

    public IEnumerable<PageListing> Listed => _recording.GetProperty("listed").EnumerateArray().Select(listed => new PageListing(
        listed.GetProperty("id").GetString()!,
        listed.GetProperty("name").GetString()!,
        listed.GetProperty("address").GetString()!,
        listed.GetProperty("readable").GetBoolean()));

    public Task<SelectedThings> SelectAsync(string selectorJson, CancellationToken cancellation)
    {
        if (!_recording.GetProperty("selections").TryGetProperty(selectorJson, out var selected))
            throw new InvalidOperationException($"The recording holds no Things for the selector {selectorJson}");
        return Task.FromResult(new SelectedThings(selected.GetProperty("things").GetRawText(), selected.GetProperty("relationships").GetRawText()));
    }

    public Task<string> ThingsInStateAsync(string state, string? narrowingJson, CancellationToken cancellation) =>
        Task.FromResult(Answered("thingsInState", JsonSerializer.Serialize(state), narrowingJson ?? "null"));

    public Task<string?> ThingRangesAsync(string thingId, CancellationToken cancellation)
    {
        var answer = Answered("thingRanges", JsonSerializer.Serialize(thingId));
        return Task.FromResult(answer == "null" ? null : answer);
    }

    public Task<string> AggregateAsync(string queryJson, CancellationToken cancellation) => Task.FromResult(Answered("aggregate", queryJson));

    public Task<string> ReduceAsync(string queryJson, CancellationToken cancellation) => Task.FromResult(Answered("reduce", queryJson));

    public Task<string> FromServiceAsync(string endpoint, string? bodyJson, CancellationToken cancellation) =>
        Task.FromResult(Answered("fromService", JsonSerializer.Serialize(endpoint), bodyJson ?? "null"));

    private string Answered(string read, params string[] argumentsJson)
    {
        var asked = $"{read} [{string.Join(",", argumentsJson)}]";
        if (_recording.GetProperty("reads").TryGetProperty(asked, out var answer)) return answer.GetString()!;
        if (_recording.GetProperty("refusedReads").TryGetProperty(asked, out var refusal)) throw new HttpRequestException(refusal.GetString());
        throw new InvalidOperationException($"The recording holds no answer for {asked}");
    }
}

internal sealed class RecordedPage(JsonElement page)
{
    public string Id => page.GetProperty("pageId").GetString()!;

    public IReadOnlyList<PageScope> Scopes => page.GetProperty("scopes").EnumerateArray()
        .Select(scope => new PageScope(scope.GetProperty("id").GetString()!, scope.GetProperty("name").GetString()!))
        .ToList();

    /// <summary>The page as Node resolved it for one scope, or for none.</summary>
    public JsonElement AnswerFor(string? scopeId) => page.GetProperty("answers").GetProperty(scopeId ?? "");
}
