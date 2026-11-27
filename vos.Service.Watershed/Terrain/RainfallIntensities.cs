namespace vos.Service.Watershed.Terrain;

public sealed record RainfallIntensity(double DurationMinutes, double MillimetresPerHour);

public sealed class RainfallIntensities(IEnumerable<RainfallIntensity> table)
{
    private readonly RainfallIntensity[] _byDuration = [.. table.OrderBy(intensity => intensity.DurationMinutes)];

    public double MillimetresPerHourOver(double minutes) => 0;
}
