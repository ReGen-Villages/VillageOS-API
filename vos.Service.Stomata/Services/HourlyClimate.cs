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
        IReadOnlyList<Reading> windDirection, IReadOnlyList<Reading> particulateMatterTen, IReadOnlyList<Reading> particulateMatterTwoAndAHalf) =>
        [];
}
