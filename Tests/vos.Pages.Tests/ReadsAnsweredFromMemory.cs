using System.Collections.Concurrent;
using System.Text.Json;

namespace vos.Pages.Tests;

// Answers every selector with the whole hand-built model and keeps what it was asked.
internal sealed class ReadsAnsweredFromMemory : IPageReads
{
    private readonly SelectedThings _selected = HandBuiltModel.Selected();

    public ConcurrentQueue<string> Selectors { get; } = new();
    public ConcurrentQueue<string> Got { get; } = new();
    public ConcurrentQueue<(string Path, string BodyJson)> Posted { get; } = new();
    public int ReservoirsRunningLow { get; init; } = 2;
    public Task EachReadWaitsFor { get; init; } = Task.CompletedTask;

    public Task<SelectedThings> SelectAsync(string selectorJson, CancellationToken cancellation)
    {
        Selectors.Enqueue(selectorJson);
        return Task.FromResult(_selected);
    }

    public async Task<string> GetAsync(string path, CancellationToken cancellation)
    {
        Got.Enqueue(path);
        await EachReadWaitsFor;
        if (path.StartsWith($"/api/states/{HandBuiltModel.RunningLow}/things", StringComparison.Ordinal))
            return JsonSerializer.Serialize(new { StateName = HandBuiltModel.RunningLow, Count = ReservoirsRunningLow });
        throw new HttpRequestException($"The hand-built model answers nothing at {path}");
    }

    public Task<string> PostAsync(string path, string bodyJson, CancellationToken cancellation)
    {
        Posted.Enqueue((path, bodyJson));
        throw new HttpRequestException($"The hand-built model answers nothing at {path}");
    }
}
