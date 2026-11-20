using System.Collections.Concurrent;
using System.Text.Json;

namespace vos.Pages.Tests;

// Answers every selector with the whole hand-built model and keeps what it was asked.
internal sealed class ReadsAnsweredFromMemory : IPageReads
{
    private readonly SelectedThings _selected = HandBuiltModel.Selected();

    public ConcurrentQueue<string> Selectors { get; } = new();
    public ConcurrentQueue<(string State, string? NarrowingJson)> StatesAsked { get; } = new();
    public int ReservoirsRunningLow { get; init; } = 2;
    public Task EachReadWaitsFor { get; init; } = Task.CompletedTask;

    public Task<SelectedThings> SelectAsync(string selectorJson, CancellationToken cancellation)
    {
        Selectors.Enqueue(selectorJson);
        return Task.FromResult(_selected);
    }

    public async Task<string> ThingsInStateAsync(string state, string? narrowingJson, CancellationToken cancellation)
    {
        StatesAsked.Enqueue((state, narrowingJson));
        await EachReadWaitsFor;
        return JsonSerializer.Serialize(new { StateName = state, Count = ReservoirsRunningLow });
    }

    public Task<string?> ThingRangesAsync(string thingId, CancellationToken cancellation) => Task.FromResult<string?>(null);

    public Task<string> AggregateAsync(string queryJson, CancellationToken cancellation) =>
        Task.FromResult($$"""{"asked":"aggregate","query":{{queryJson}}}""");

    public Task<string> ReduceAsync(string queryJson, CancellationToken cancellation) =>
        Task.FromResult($$"""{"asked":"reduce","query":{{queryJson}}}""");

    public Task<string> FromServiceAsync(string endpoint, string? bodyJson, CancellationToken cancellation) =>
        Task.FromResult($$"""{"asked":"{{endpoint}}","body":{{bodyJson ?? "null"}}}""");
}
