using System.Collections.Concurrent;

namespace vos.Service.Delta.Services;

/// <summary>What Delta has provisioned in each model it has served. One Delta process answers every
/// project — a second project's call reaches the daemon already running on the port both models
/// declare — so the catalog cannot be a single set of Things: a registration is written to the model
/// of whoever called, and its template has to be in that same model to be wired to.
///
/// A model is provisioned once, on its first registration, under the token that named it.</summary>
public sealed class ModelTemplateCatalog
{
    /// <summary>The key for a token that names no model, which happens only where there is one.</summary>
    public static readonly Guid UnnamedModel = Guid.Empty;

    private readonly ConcurrentDictionary<Guid, ModelEntry> _byModel = new();

    public async Task<ProvisionedCatalog?> ProvisionedForAsync(Guid modelId, Func<Task<ProvisionedCatalog?>> provision)
    {
        var entry = _byModel.GetOrAdd(modelId, _ => new ModelEntry());
        if (entry.Catalog != null)
            return entry.Catalog;

        // Mycelium accepts a second Thing carrying a name it already holds, and then answers every
        // lookup for that name with a conflict no later run can repair. Two calls arriving together
        // from a model nobody has served yet would each provision it, so they are made to queue.
        await entry.FirstContact.WaitAsync();
        try
        {
            // Null is not cached: a model whose provisioning failed is retried on the next call.
            return entry.Catalog ??= await provision();
        }
        finally
        {
            entry.FirstContact.Release();
        }
    }

    /// <summary>Discard what was provisioned for a model, so its next registration provisions again.
    /// A held Thing id survives the Thing being deleted, and every later registration in that model
    /// would fail against it.</summary>
    public void Forget(Guid modelId) => _byModel.TryRemove(modelId, out _);

    private sealed class ModelEntry
    {
        public SemaphoreSlim FirstContact { get; } = new(1, 1);
        public ProvisionedCatalog? Catalog { get; set; }
    }
}
