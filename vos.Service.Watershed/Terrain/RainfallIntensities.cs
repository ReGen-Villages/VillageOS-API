namespace vos.Service.Watershed.Terrain;

public sealed record RainfallIntensity(double DurationMinutes, double MillimetresPerHour);

public sealed class RainfallIntensities(IEnumerable<RainfallIntensity> table)
{
    private readonly RainfallIntensity[] _byDuration = [.. table.OrderBy(intensity => intensity.DurationMinutes)];

    public double MillimetresPerHourOver(double minutes)
    {
        var held = Math.Clamp(minutes, _byDuration[0].DurationMinutes, _byDuration[^1].DurationMinutes);
        for (var index = 0; index + 1 < _byDuration.Length; index++)
        {
            var (shorter, longer) = (_byDuration[index], _byDuration[index + 1]);
            if (held > longer.DurationMinutes) continue;
            var fraction = Math.Log(held / shorter.DurationMinutes) / Math.Log(longer.DurationMinutes / shorter.DurationMinutes);
            return Math.Exp(Math.Log(shorter.MillimetresPerHour) + fraction * (Math.Log(longer.MillimetresPerHour) - Math.Log(shorter.MillimetresPerHour)));
        }
        return _byDuration[^1].MillimetresPerHour;
    }
}
