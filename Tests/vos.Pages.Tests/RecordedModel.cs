using System.Text.Json;

namespace vos.Pages.Tests;

// A question the recording holds no answer for fails the test that asked it.
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

    public IEnumerable<RecordedThing> Things => _recording.GetProperty("things").EnumerateArray().Select(thing => new RecordedThing(thing));

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

    public Task<string> GetAsync(string path, CancellationToken cancellation) => Task.FromResult(Answered($"GET {path}"));

    public Task<string> PostAsync(string path, string bodyJson, CancellationToken cancellation) => Task.FromResult(Answered($"POST {path} {bodyJson}"));

    private string Answered(string asked)
    {
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

    // The page as Node resolved it for one scope, or for none.
    public JsonElement AnswerFor(string? scopeId) => page.GetProperty("answers").GetProperty(scopeId ?? "");
}

/// <summary>One Thing with derived figures, as the reader in Node answered each question about it.</summary>
internal sealed class RecordedThing(JsonElement thing)
{
    public string Id => thing.GetProperty("thingId").GetString()!;

    public JsonElement Inputs => thing.GetProperty("inputs");

    public IEnumerable<(string Figure, JsonElement Explained)> Explained =>
        thing.GetProperty("explained").EnumerateObject().Select(figure => (figure.Name, figure.Value));

    public IEnumerable<(string Property, JsonElement Origin)> Origins =>
        thing.GetProperty("origins").EnumerateObject().Select(property => (property.Name, property.Value));
}
