namespace vos.Service.Stomata.Services;

public readonly record struct Reading(DateTime At, double Value);

public sealed record ClimateHour(
    DateTime At,
    double? TemperatureCelsius,
    double? RelativeHumidityPercent,
    double? WindSpeedMetresPerSecond,
    double? WindDirectionDegrees,
    double? ParticulateMatterTenMicrograms,
    double? ParticulateMatterTwoAndAHalfMicrograms);

public static class HourlyClimate
{
    public static IReadOnlyList<ClimateHour> Join(
        IReadOnlyList<Reading> temperature, IReadOnlyList<Reading> relativeHumidity, IReadOnlyList<Reading> windSpeed,
        IReadOnlyList<Reading> windDirection, IReadOnlyList<Reading> particulateMatterTen, IReadOnlyList<Reading> particulateMatterTwoAndAHalf)
    {
        var series = new[] { temperature, relativeHumidity, windSpeed, windDirection, particulateMatterTen, particulateMatterTwoAndAHalf }
            .Select(LastReceivedAtEachInstant)
            .ToArray();
        return
        [
            .. series.SelectMany(byInstant => byInstant.Keys).Distinct().Order().Select(at => new ClimateHour(
                at, ValueAt(series[0], at), ValueAt(series[1], at), ValueAt(series[2], at),
                ValueAt(series[3], at), ValueAt(series[4], at), ValueAt(series[5], at))),
        ];
    }

    private static Dictionary<DateTime, double> LastReceivedAtEachInstant(IReadOnlyList<Reading> readings)
    {
        var byInstant = new Dictionary<DateTime, double>(readings.Count);
        foreach (var reading in readings) byInstant[reading.At] = reading.Value;
        return byInstant;
    }

    private static double? ValueAt(Dictionary<DateTime, double> byInstant, DateTime at) =>
        byInstant.TryGetValue(at, out var value) ? value : null;
}
