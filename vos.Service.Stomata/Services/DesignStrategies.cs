namespace vos.Service.Stomata.Services;

public interface IThermalComfort
{
    double PredictedMeanVote(double airCelsius, double relativeHumidityPercent, double clothingClo, double airSpeedMetresPerSecond);
}

public sealed record ComfortAssumptions(
    double SummerClothingClo,
    double WinterClothingClo,
    double StillAirMetresPerSecond,
    double AirMovementMetresPerSecond,
    double HumidityRatioLimitGramsPerKilogram,
    double IndirectEvaporativeStageShare,
    double PressureKilopascals);

public enum DesignStrategy
{
    ComfortableAsItIs,
    WithAirMovement,
    DehumidificationNeeded,
    HeatingNeeded,
    DirectEvaporativeCooling,
    TwoStageEvaporativeCooling,
    CoolingSystemNeeded,
}

public sealed class DesignStrategies(IThermalComfort comfort, ComfortAssumptions assumptions)
{
    public ComfortAssumptions Assumptions { get; } = assumptions;

    public double HottestComfortableWetBulbCelsius => double.NaN;

    public DesignStrategy Of(double airCelsius, double relativeHumidityPercent) => DesignStrategy.CoolingSystemNeeded;

    public (double CoolCelsius, double WarmCelsius) ZoneAt(double humidityRatioGramsPerKilogram, double airSpeedMetresPerSecond) =>
        (double.NaN, double.NaN);

    public bool IsWarmerThanTheSummerZone(double airCelsius, double humidityRatioGramsPerKilogram) => false;

    public bool DirectEvaporationWorks(double airCelsius, double humidityRatioGramsPerKilogram) => false;

    public bool TwoStageEvaporationWorks(double airCelsius, double humidityRatioGramsPerKilogram) => false;
}
