namespace vos.Service.Stomata.Services;

public sealed class StandardComfort : IThermalComfort
{
    public double PredictedMeanVote(
        double airCelsius, double relativeHumidityPercent, double clothingClo, double airSpeedMetresPerSecond, double metabolicRateMet) =>
        throw new NotImplementedException();
}
