using System.Collections.Concurrent;
using vos.PlatformClient;

namespace vos.Hypha.Registry;

public sealed class ModelNotReachedException(string message) : Exception(message);

// A client is kept only for a model Mycelium lists for the key, so the number kept is bounded by the models
// the key may reach.
public sealed class ModelClients(MyceliumClient own)
{
    public const string Parameter = "model";

    public const string ParameterDescription =
        "The model to ask, as an identifier or a name, as list_models gives them: the server's own when left out, "
        + "and required when the key reaches more than one model and names none.";

    private readonly ConcurrentDictionary<Guid, MyceliumClient> _byModel = new();

    public async Task<MyceliumClient> ForAsync(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return own;

        var reachable = await ReachableAsync();
        var asked = reachable
            .Where(listed => Guid.TryParse(model, out var id) ? listed.Id == id : listed.Name == model)
            .Select(listed => listed.Id)
            .ToList();

        return asked.Count switch
        {
            1 => For(asked[0]),
            0 => throw new ModelNotReachedException(
                $"The key reaches no model '{model}'. It reaches: {string.Join(", ", reachable.Select(listed => listed.Name))}."),
            _ => throw new ModelNotReachedException(
                $"More than one model is named '{model}'. Give its identifier: {string.Join(", ", asked)}."),
        };
    }

    public MyceliumClient For(Guid model) => _byModel.GetOrAdd(model, own.ForModel);

    // A key that names no model is refused the token the list needs, and the refusal lists the models instead.
    public async Task<IReadOnlyList<ReachableModel>> ReachableAsync()
    {
        try
        {
            return [.. (await own.ListModelsAsync()).EnumerateArray()
                .Select(listed => new ReachableModel(listed.GetProperty("Id").GetGuid(), listed.GetProperty("Name").GetString()!))];
        }
        catch (ModelNotNamedException refused)
        {
            return refused.Models;
        }
    }
}
