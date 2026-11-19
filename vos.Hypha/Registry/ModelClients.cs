using System.Collections.Concurrent;
using vos.Taproot;

namespace vos.Hypha.Registry;

public sealed class ModelNotReachedException(string message) : Exception(message);

// A client is kept only for a model Mycelium lists for the key, so the number kept is bounded by the models
// the key may reach.
public sealed class ModelClients(MyceliumClient own)
{
    public const string Parameter = "model";

    private readonly ConcurrentDictionary<Guid, MyceliumClient> _byModel = new();

    public async Task<MyceliumClient> ForAsync(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return own;

        var reachable = (await own.ListModelsAsync()).EnumerateArray()
            .Select(listed => (Id: listed.GetProperty("Id").GetGuid(), Name: listed.GetProperty("Name").GetString()))
            .ToList();
        var asked = reachable
            .Where(listed => Guid.TryParse(model, out var id) ? listed.Id == id : listed.Name == model)
            .Select(listed => listed.Id)
            .ToList();

        return asked.Count switch
        {
            1 => _byModel.GetOrAdd(asked[0], own.ForModel),
            0 => throw new ModelNotReachedException(
                $"The key reaches no model '{model}'. It reaches: {string.Join(", ", reachable.Select(listed => listed.Name))}."),
            _ => throw new ModelNotReachedException(
                $"More than one model is named '{model}'. Give its identifier: {string.Join(", ", asked)}."),
        };
    }
}
