using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using vos.Pages;
using vos.PlatformClient;

namespace vos.Hypha.Tools.Pages;

// Every path comes out of a page description, which is the model's data, so each is checked against PageReadRoutes before it is sent.
public sealed class MyceliumPageReads(MyceliumClient mycelium) : IPageReads
{
    // The reader reads a binding it could not ask as nothing, so the tool says from here what was refused.
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
