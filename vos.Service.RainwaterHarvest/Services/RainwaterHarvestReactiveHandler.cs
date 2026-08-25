using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.RainwaterHarvest.Services;

// The reactive form, and the only one (#6020): a relationship naming the study makes this read the
// study's effective properties, compute, and write the harvest, what each demand asked for, how much of
// it was covered and what is left short, back onto the study as Facts.
//
// Both footprints are written by LandAllocation onto the same study, which puts this on the second layer
// of the analysis: one dispatch, and every later move of either footprint recomputes on its own.
//
// Only three inputs are named here — the ones the harvest volume is worked out from, which is this
// service's own arithmetic and nothing the model can express. Everything about the demands comes from
// the model: which ones there are, the order they are served in, and which properties each is read from
// and written to.
public sealed class RainwaterHarvestReactiveHandler : MyceliumClientBase
{
    private readonly ISubscriptionClient _subscriptions;

    public RainwaterHarvestReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<RainwaterHarvestReactiveHandler> logger,
        string myceliumUrl, string? serviceToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey) =>
        _subscriptions = new SubscriptionClient(httpClientFactory, logger, myceliumUrl, serviceToken, apiKey: apiKey);

    /// <summary>The subscription client is supplied rather than built, so a test can hand it a snapshot
    /// instead of a broker.</summary>
    public RainwaterHarvestReactiveHandler(
        IHttpClientFactory httpClientFactory, ILogger<RainwaterHarvestReactiveHandler> logger,
        string myceliumUrl, string? serviceToken, ISubscriptionClient subscriptions)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken) => _subscriptions = subscriptions;

    // Private, because HarvestInputs below already publishes the same names as the set a caller reads.
    private const string BuiltFootprintInput = "builtFootprintHectares";
    private const string RainfallInput = "rainfallMillimetresPerYear";
    private const string RunoffCoefficientInput = "runoffCoefficient";

    public const string HarvestOutput = "harvestM3PerYear";
    public const string TotalWaterDemandOutput = "totalWaterDemandM3PerYear";
    public const string PctOfWaterDemandOutput = "pctOfWaterDemand";

    /// <summary>What this handler reads off the study itself, whatever demands the model puts on the
    /// harvest. The figures the demands are sized by are named by the model, not here.</summary>
    public static readonly IReadOnlySet<string> InputProperties = new HashSet<string>(
        [BuiltFootprintInput, RainfallInput, RunoffCoefficientInput], StringComparer.Ordinal);

    private volatile IReadOnlySet<string> _watched = InputProperties;

    /// <summary>Everything a change to which has to recompute: what this handler reads itself, and what the
    /// last recompute found the model sizes its demands by. A set fixed here would go stale the moment a
    /// demand named a property it had never heard of.</summary>
    public IReadOnlySet<string> WatchedProperties => _watched;

    public async Task<RainwaterHarvestOutputs> RecomputeAsync(
        Guid studyId, CancellationToken cancellationToken = default)
    {
        var components = await ReadDemandComponentsAsync(cancellationToken);
        var watched = Watched(components);
        RefuseComponentsWritingOntoAnythingItWakesOn(components, watched);

        var properties = new StudyProperties(
            await CreateAuthenticatedClientAsync(TimeSpan.FromSeconds(10)), MyceliumUrl, "RainwaterHarvest");

        var inputs = await properties.ReadAsync(studyId, cancellationToken);
        var result = RainwaterHarvestCalculator.Compute(new RainwaterHarvestInputs(
            BuiltFootprintHectares: inputs.Number(BuiltFootprintInput),
            RainfallMillimetresPerYear: inputs.Number(RainfallInput),
            RunoffCoefficient: inputs.Number(RunoffCoefficientInput),
            Demands: [.. components.Select(component => new DemandToServe(
                component.Name,
                inputs.Number(component.QuantityProperty),
                inputs.Number(component.RateProperty)))]));

        _watched = watched;

        await properties.WriteAsync(studyId, HarvestOutput, result.HarvestM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, TotalWaterDemandOutput, result.TotalWaterDemandM3PerYear, cancellationToken);
        await properties.WriteAsync(studyId, PctOfWaterDemandOutput, result.PctOfWaterDemand, cancellationToken);

        var byName = components.ToDictionary(component => component.Name, StringComparer.Ordinal);
        foreach (var demand in result.Served)
        {
            var component = byName[demand.Name];
            await properties.WriteAsync(studyId, component.DemandProperty, demand.DemandM3PerYear, cancellationToken);
            await properties.WriteAsync(studyId, component.CoverageProperty, demand.PctCovered, cancellationToken);
            await properties.WriteAsync(studyId, component.ShortfallProperty, demand.ShortfallM3PerYear, cancellationToken);
        }

        return result;
    }

    private async Task<IReadOnlyList<WaterDemandComponent>> ReadDemandComponentsAsync(
        CancellationToken cancellationToken)
    {
        var subscribed = await _subscriptions.SubscribeAsync(
            WaterDemandComponentReader.Selector, cancellationToken);
        try
        {
            return WaterDemandComponentReader.Read(subscribed.Snapshot);
        }
        finally
        {
            // The read already succeeded; failing to release the subscription must not lose it.
            try { await _subscriptions.UnsubscribeAsync(subscribed.SubscriptionId, cancellationToken); }
            catch (Exception exception)
            {
                Logger.LogWarning(exception, "RainwaterHarvest could not release the subscription {SubscriptionId}",
                    subscribed.SubscriptionId);
            }
        }
    }

    // A component writing onto something this service wakes on would make it wake itself, recompute, and
    // wake itself again for as long as the model held that spelling. Checked against the whole watched
    // set rather than against this service's own three, because one component's answer landing on another
    // component's quantity loops just as tightly. Refused by name before anything is written, because the
    // loop itself leaves nothing behind saying which component caused it.
    private static void RefuseComponentsWritingOntoAnythingItWakesOn(
        IReadOnlyList<WaterDemandComponent> components, IReadOnlySet<string> watched)
    {
        var offending = components
            .SelectMany(component => new[]
                { component.DemandProperty, component.CoverageProperty, component.ShortfallProperty }
                .Where(watched.Contains)
                .Select(written => $"'{component.Name}' writes onto '{written}'"))
            .ToList();

        if (offending.Count > 0)
            throw new InvalidOperationException(
                string.Join("; ", offending)
                + ", which this harvest reads. Writing an answer onto an input would recompute the study "
                + "for as long as the model held that spelling.");
    }

    private static IReadOnlySet<string> Watched(IReadOnlyList<WaterDemandComponent> components) =>
        new HashSet<string>(
            InputProperties.Concat(components.SelectMany(component =>
                new[] { component.QuantityProperty, component.RateProperty })),
            StringComparer.Ordinal);
}
