using System.Text.Json;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public sealed record WindSector(Guid Id, double CentreDegrees);

public sealed record WindSpeedBand(Guid Id, double? AtLeastMetresPerSecond, double? BelowMetresPerSecond);

public sealed class WindRose(
    IReadOnlyList<WindSector> sectors, IReadOnlyList<WindSpeedBand> bands, IReadOnlyDictionary<(Guid Sector, Guid Band), Guid> cells)
{
    public const string IsPredicateName = "is";
    public const string CentreDegreesProperty = "centreDegrees";
    public const string AtLeastProperty = "atLeastMetresPerSecond";
    public const string BelowProperty = "belowMetresPerSecond";

    // The provider gives still air a direction, which is no measurement, so a calm hour has no cell.
    public Guid? CellOf(double? directionDegrees, double? speedMetresPerSecond)
    {
        if (directionDegrees is not { } direction || speedMetresPerSecond is not { } speed || speed <= 0)
            return null;

        var width = 360.0 / sectors.Count;
        var sector = sectors.FirstOrDefault(candidate => Modulo(direction - candidate.CentreDegrees + width / 2, 360) < width);
        var band = bands.FirstOrDefault(candidate =>
            (candidate.AtLeastMetresPerSecond is not { } lower || speed >= lower)
            && (candidate.BelowMetresPerSecond is not { } upper || speed < upper));
        return sector is not null && band is not null && cells.TryGetValue((sector.Id, band.Id), out var cell) ? cell : null;
    }

    // Each cell is matched to its sector and its class by what it relates to, so the predicates between them may be named anything.
    public static WindRose From(SnapshotDocument snapshot)
    {
        var isPredicates = snapshot.Things.Where(thing => thing.Name == IsPredicateName).Select(thing => thing.Id).ToHashSet();

        IReadOnlyList<SnapshotThing> MembersOf(Role role)
        {
            if (snapshot.Bound(role) is not [var kind]) return [];
            var members = snapshot.Relationships
                .Where(edge => edge.TargetId == kind.Id && isPredicates.Contains(edge.PredicateId))
                .Select(edge => edge.SubjectId)
                .ToHashSet();
            return [.. snapshot.Things.Where(thing => !thing.IsArchetype && members.Contains(thing.Id))];
        }

        var sectors = MembersOf(StomataRoles.WindDirectionSector)
            .Where(sector => Number(sector, CentreDegreesProperty) is not null)
            .Select(sector => new WindSector(sector.Id, Number(sector, CentreDegreesProperty)!.Value))
            .ToList();
        var bands = MembersOf(StomataRoles.WindSpeedClass)
            .Select(band => new WindSpeedBand(band.Id, Number(band, AtLeastProperty), Number(band, BelowProperty)))
            .ToList();

        var sectorIds = sectors.Select(sector => sector.Id).ToHashSet();
        var bandIds = bands.Select(band => band.Id).ToHashSet();
        var cells = new Dictionary<(Guid Sector, Guid Band), Guid>();
        foreach (var cell in MembersOf(StomataRoles.WindRoseCell))
        {
            var reached = snapshot.Relationships.Where(edge => edge.SubjectId == cell.Id).Select(edge => edge.TargetId).ToList();
            if (reached.Where(sectorIds.Contains).ToList() is [var sector] && reached.Where(bandIds.Contains).ToList() is [var band])
                cells[(sector, band)] = cell.Id;
        }
        return new WindRose(sectors, bands, cells);
    }

    private static double? Number(SnapshotThing thing, string property) =>
        thing.Properties.TryGetValue(property, out var held) && held.Value.ValueKind == JsonValueKind.Number ? held.Value.GetDouble() : null;

    private static double Modulo(double value, double divisor) => (value % divisor + divisor) % divisor;
}
