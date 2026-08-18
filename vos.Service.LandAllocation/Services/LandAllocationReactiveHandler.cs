using System.Net.Http.Json;
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
// Where the results go follows the same rule: a category's area belongs to that category's allocation,
// and only the two footprints — which are about the whole parcel — belong to the study.
public sealed class LandAllocationReactiveHandler : MyceliumClientBase
{
    private readonly ISubscriptionClient _subscriptions;

    public LandAllocationReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<LandAllocationReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) =>
        _subscriptions = new SubscriptionClient(httpClientFactory, logger, myceliumUrl, serviceToken);

    /// <summary>The subscription client is supplied rather than built, so a test can hand it a snapshot
    /// instead of a broker.</summary>
    public LandAllocationReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<LandAllocationReactiveHandler> logger,
        string myceliumUrl, string? serviceToken, ISubscriptionClient subscriptions)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) => _subscriptions = subscriptions;

    public const string BuiltFootprintOutput = "builtFootprintHectares";
    public const string ProductiveFootprintOutput = "productiveFootprintHectares";
    public const string AllocatedAreaOutput = "allocatedAreaHectares";
    public const string NormalisedShareOutput = "normalisedSharePct";

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

        var client = await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(30));
        foreach (var (category, area) in result.AreaByCategory)
        {
            var allocationId = split.AllocationIdByCategory[category];
            await WriteAsync(client, allocationId, AllocatedAreaOutput, area, cancellationToken);
            await WriteAsync(client, allocationId, NormalisedShareOutput, result.NormalisedSharePct[category], cancellationToken);
        }

        await WriteAsync(client, studyId, BuiltFootprintOutput, result.BuiltFootprintHectares, cancellationToken);
        await WriteAsync(client, studyId, ProductiveFootprintOutput, result.ProductiveFootprintHectares, cancellationToken);
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

    private async Task WriteAsync(
        HttpClient client, Guid thingId, string property, double value, CancellationToken cancellationToken)
    {
        var path = $"{MyceliumUrl}/api/things/{thingId}/properties/{Uri.EscapeDataString(property)}/facts";
        var response = await client.PostAsJsonAsync(path, new { value }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"LandAllocation could not write {thingId}.{property} ({(int)response.StatusCode} {response.StatusCode})");
    }
}
