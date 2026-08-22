namespace vos.Service.LandAllocation.Services;

/// <summary>One category of the programme split: what the model calls it, the share it was given, and
/// whether the model marks it as hard surface, as growing land, or as both.</summary>
public sealed record AllocatedCategory(string Name, double SharePct, bool IsBuilt, bool IsProductive);

public sealed record LandAllocationInputs(double ParcelAreaHectares, IReadOnlyList<AllocatedCategory> Categories);

public sealed record LandAllocationOutputs(
    IReadOnlyDictionary<string, double> AreaByCategory,
    IReadOnlyDictionary<string, double> NormalisedSharePct,
    double BuiltFootprintHectares,
    double ProductiveFootprintHectares);

// Turns the programme split into areas, and derives the two footprints the balances read.
//
// Shares are normalised across the categories selected, so they describe the whole parcel however they
// were written down — a planner who moves a slider is stating a proportion, not a figure that has to add
// up. The footprints are sums over the categories the model marks, never over names known here: which
// land sheds rainwater and which grows food is a fact about a programme, and a project whose programme
// divides differently moves a flag.
//
// The two footprints are not a partition of the parcel. A category may be marked for both — a roofed
// growing area is hard surface the rain runs off and land that grows food — so they can overlap and
// together exceed the parcel. What must sum to the parcel is the per-category areas.
public static class LandAllocationCalculator
{
    public static LandAllocationOutputs Compute(LandAllocationInputs input)
    {
        foreach (var category in input.Categories)
            if (category.SharePct < 0)
                throw new ArgumentOutOfRangeException(nameof(input),
                    $"'{category.Name}' was given a share of {category.SharePct}. A negative share would take "
                    + "area from the categories beside it rather than describing any of the parcel.");

        var total = input.Categories.Sum(category => category.SharePct);

        // Every share left at zero is a split that describes none of the parcel, not one to divide by.
        var share = total > 0
            ? input.Categories.ToDictionary(c => c.Name, c => c.SharePct / total * 100.0)
            : input.Categories.ToDictionary(c => c.Name, _ => 0.0);

        var area = share.ToDictionary(entry => entry.Key, entry => entry.Value / 100.0 * input.ParcelAreaHectares);

        return new LandAllocationOutputs(
            area,
            share,
            FootprintOf(input.Categories, area, category => category.IsBuilt),
            FootprintOf(input.Categories, area, category => category.IsProductive));
    }

    private static double FootprintOf(
        IReadOnlyList<AllocatedCategory> categories, IReadOnlyDictionary<string, double> area,
        Func<AllocatedCategory, bool> marked) =>
        categories.Where(marked).Sum(category => area[category.Name]);
}
