namespace vos.Service.Stomata.Services;

public sealed class StandardComfort : IThermalComfort
{
    public const double StillAirMetresPerSecond = 0.1;
    public const double ElevatedAirSpeedAboveMetresPerSecond = 0.2;

    public double PredictedMeanVote(
        double airCelsius, double relativeHumidityPercent, double clothingClo, double airSpeedMetresPerSecond, double metabolicRateMet) =>
        throw new NotImplementedException();

    public static double FangerPredictedMeanVote(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo) =>
        throw new NotImplementedException();

    public static double StandardEffectiveTemperatureCelsius(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo) =>
        throw new NotImplementedException();

    public static double CoolingEffectCelsius(
        double airCelsius, double meanRadiantCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond,
        double metabolicRateMet, double clothingClo) =>
        throw new NotImplementedException();
}
