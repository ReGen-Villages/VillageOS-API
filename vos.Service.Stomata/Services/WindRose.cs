using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public sealed record WindSector(Guid Id, double CentreDegrees);

public sealed record WindSpeedBand(Guid Id, double? AtLeastMetresPerSecond, double? BelowMetresPerSecond);

public sealed class WindRose(
    IReadOnlyList<WindSector> sectors, IReadOnlyList<WindSpeedBand> bands, IReadOnlyDictionary<(Guid Sector, Guid Band), Guid> cells)
{
    public Guid? CellOf(double? directionDegrees, double? speedMetresPerSecond) => null;

    public static WindRose From(SnapshotDocument snapshot) => new([], [], new Dictionary<(Guid, Guid), Guid>());
}
