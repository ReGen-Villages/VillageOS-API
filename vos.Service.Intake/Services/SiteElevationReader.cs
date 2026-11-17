using System.Text.Json;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public sealed record HeldElevation(string? Boundary, IReadOnlyList<string> FiguresStated)
{
    // The explore page posts the whole submission again whenever a dial settles, and the elevation service's
    // free tier allows few calls a day across the platform, so a border is looked up once rather than on every save.
    public bool NeedsLookupFor(IReadOnlyList<BoundaryPoint> postedBorder) =>
        BorderMovedFrom(postedBorder) || FiguresStated.Count < SiteElevationReader.Figures.Count;

    public bool BorderMovedFrom(IReadOnlyList<BoundaryPoint> postedBorder) =>
        Boundary != BoundaryGeometry.ToGeoJson(postedBorder);
}

public static class SiteElevationReader
{
    public const string CentreFigure = "elevationMetres";
    public const string LowestFigure = "elevationMinimumMetres";
    public const string HighestFigure = "elevationMaximumMetres";

    public static readonly IReadOnlyList<string> Figures = [CentreFigure, LowestFigure, HighestFigure];

    public static IReadOnlyList<string> FiguresIn(AnsweredElevation? answered) =>
    [
        .. answered?.ElevationMetres is null ? [] : new[] { CentreFigure },
        .. answered?.Range is null ? [] : new[] { LowestFigure, HighestFigure },
    ];

    public const string BoundaryProperty = "boundary";

    public static SubscriptionSelector Selector(Guid siteId, Guid parcelId) => new()
    {
        Ids = [siteId, parcelId],
        IncludeIsAncestors = false,
        IncludeRelationships = false,
    };

    public static HeldElevation Read(SnapshotDocument snapshot, Guid siteId, Guid parcelId)
    {
        var site = snapshot.Things.FirstOrDefault(thing => thing.Id == siteId);
        var parcel = snapshot.Things.FirstOrDefault(thing => thing.Id == parcelId);

        var boundary = parcel?.StatedValue(BoundaryProperty) is { Value.ValueKind: JsonValueKind.String } stated
            ? stated.Value.GetString()
            : null;
        IReadOnlyList<string> figures = site is null
            ? []
            : [.. Figures.Where(name => site.StatedValue(name) is { Value.ValueKind: not JsonValueKind.Null })];
        return new HeldElevation(boundary, figures);
    }
}
