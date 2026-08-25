using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.LandAllocation.Services;

// The reactive form, and the only one (#6020): a relationship naming the study makes this read the split,
// compute, and write the areas back.
//
// It differs from the balances beside it in where its inputs live. They read everything off the study and
// watch it; the programme split lives on the allocations hanging off the site, so this names those Things
// when it watches (#6539) and re-names them on every recompute — a planner can add or remove one, and a
// set fixed at the first dispatch would go quietly stale.
//
// What it writes is the two footprints, and only those. Each allocation's own area and normalised share
// are formulas the shared analysis declares on the allocation, so the model works them out and refuses a
// written value for either. The footprints stay here because each sums the allocations whose category
// carries a mark, and a relationship path narrows by archetype rather than by a property a Thing carries,
// so no path reaches only the marked ones.
public sealed class LandAllocationReactiveHandler : MyceliumClientBase
{
    private readonly ISubscriptionClient _subscriptions;

    public LandAllocationReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<LandAllocationReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey) =>
        _subscriptions = new SubscriptionClient(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey);

    /// <summary>The subscription client is supplied rather than built, so a test can hand it a snapshot
    /// instead of a broker.</summary>
    public LandAllocationReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<LandAllocationReactiveHandler> logger,
        string myceliumUrl, string? serviceToken, ISubscriptionClient subscriptions)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) => _subscriptions = subscriptions;

    public const string BuiltFootprintOutput = "builtFootprintHectares";
    public const string ProductiveFootprintOutput = "productiveFootprintHectares";

    public static IReadOnlySet<string> InputProperties => ProgrammeSplitReader.InputProperties;

    /// <summary>The Things the last read took its inputs from, for the caller to follow. Empty until a
    /// recompute has run, which is why the dispatch that starts a study is what registers the watch.</summary>
    public IReadOnlyList<Guid> ReadsFrom { get; private set; } = [];

    public async Task<LandAllocationOutputs> RecomputeAsync(Guid studyId, CancellationToken cancellationToken = default)
    {
        var split = await ReadSplitAsync(studyId, cancellationToken);
        ReadsFrom = split.ReadsFrom;

        if (split.Uncategorised.Count > 0)
            throw new InvalidOperationException(
                $"The study {studyId} holds allocations naming no category the model declares: "
                + string.Join(", ", split.Uncategorised)
                + ". Allocating the rest would describe a different parcel than the one submitted.");

        var result = LandAllocationCalculator.Compute(
            new LandAllocationInputs(split.ParcelAreaHectares, split.Categories));

        var properties = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30)), MyceliumUrl, "LandAllocation");

        await properties.WriteAsync(studyId, BuiltFootprintOutput, result.BuiltFootprintHectares, cancellationToken);
        await properties.WriteAsync(studyId, ProductiveFootprintOutput, result.ProductiveFootprintHectares, cancellationToken);
        return result;
    }

    private async Task<ProgrammeSplit> ReadSplitAsync(Guid studyId, CancellationToken cancellationToken)
    {
        var subscribed = await _subscriptions.SubscribeAsync(
            ProgrammeSplitReader.SelectorFor(studyId), cancellationToken);
        try
        {
            return ProgrammeSplitReader.Read(subscribed.Snapshot, studyId);
        }
        finally
        {
            // The read already succeeded; failing to release the subscription must not lose it.
            try { await _subscriptions.UnsubscribeAsync(subscribed.SubscriptionId, cancellationToken); }
            catch (Exception exception)
            {
                Logger.LogWarning(exception, "LandAllocation could not release the subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }
}
