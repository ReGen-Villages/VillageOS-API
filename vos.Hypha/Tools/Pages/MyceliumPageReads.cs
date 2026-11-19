using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using vos.MyceliumClient;
using vos.Pages;

namespace vos.Hypha.Tools.Pages;

/// <summary>
/// Answers a page read from Mycelium with the key this server holds. It sends only the routes a page
/// read needs and refuses every other path, because the path comes out of a page description, which is
/// the model's data.
/// </summary>
public sealed class MyceliumPageReads(MyceliumClient mycelium) : IPageReads
{
    /// <summary>What was refused during the call, for the tool to say in its answer: the reader itself
    /// reads a binding it could not ask as nothing.</summary>
    public ConcurrentQueue<string> Refused { get; } = new();

    public async Task<SelectedThings> SelectAsync(string selectorJson, CancellationToken cancellation)
    {
        var answer = await mycelium.ReadSelectionAsync(JsonNode.Parse(selectorJson)!);
        var snapshot = answer.GetProperty("snapshot");
        return new SelectedThings(snapshot.GetProperty("things").GetRawText(), snapshot.GetProperty("relationships").GetRawText());
    }

    public Task<string> GetAsync(string path, CancellationToken cancellation) =>
        Refusing(PageReadRoutes.RefusalToGet(path)) ?? mycelium.RequestAsync(HttpMethod.Get, path);

    public Task<string> PostAsync(string path, string bodyJson, CancellationToken cancellation) =>
        Refusing(PageReadRoutes.RefusalToPost(path)) ?? mycelium.RequestAsync(HttpMethod.Post, path, bodyJson);

    private Task<string>? Refusing(string? refusal)
    {
        if (refusal is null) return null;
        Refused.Enqueue(refusal);
        return Task.FromException<string>(new InvalidOperationException(refusal));
    }
}
