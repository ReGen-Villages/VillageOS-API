namespace vos.Service.Stomata.Services;

public interface IThermalComfort
{
    double PredictedMeanVote(
        double airCelsius, double relativeHumidityPercent, double clothingClo, double airSpeedMetresPerSecond, double metabolicRateMet,
        double pressureKilopascals);
}

public sealed record ComfortAssumptions(
    double MetabolicRateMet,
    double SummerClothingClo,
    double WinterClothingClo,
    double StillAirMetresPerSecond,
    double AirMovementMetresPerSecond,
    double HumidityRatioLimitGramsPerKilogram,
    double IndirectEvaporativeStageShare,
    double PressureKilopascals);

// The tests an hour is put to are the Climate Consultant psychrometric chart's, as the site-study demo ports them; the groups
// that ask them, and their order, are the model's.
public sealed class DesignStrategies(IThermalComfort comfort, ComfortAssumptions assumptions)
{
    private const double ComfortableVote = 0.5;
    private const double CoolestSearchedCelsius = -20;
    private const double WarmestSearchedCelsius = 60;

    private double? _hottestComfortableWetBulb;

    public ComfortAssumptions Assumptions { get; } = assumptions;

    public double HottestComfortableWetBulbCelsius => _hottestComfortableWetBulb ??= Psychrometrics.WetBulbCelsius(
        TemperatureWhereTheVoteReaches(Assumptions.HumidityRatioLimitGramsPerKilogram, Assumptions.SummerClothingClo, Assumptions.StillAirMetresPerSecond, ComfortableVote),
        Assumptions.HumidityRatioLimitGramsPerKilogram, Assumptions.PressureKilopascals);

    public (double CoolCelsius, double WarmCelsius) ZoneAt(double humidityRatioGramsPerKilogram, double airSpeedMetresPerSecond) =>
    (
        Math.Min(TemperatureWhereTheVoteReaches(humidityRatioGramsPerKilogram, Assumptions.SummerClothingClo, airSpeedMetresPerSecond, -ComfortableVote),
            TemperatureWhereTheVoteReaches(humidityRatioGramsPerKilogram, Assumptions.WinterClothingClo, airSpeedMetresPerSecond, -ComfortableVote)),
        Math.Max(TemperatureWhereTheVoteReaches(humidityRatioGramsPerKilogram, Assumptions.SummerClothingClo, airSpeedMetresPerSecond, ComfortableVote),
            TemperatureWhereTheVoteReaches(humidityRatioGramsPerKilogram, Assumptions.WinterClothingClo, airSpeedMetresPerSecond, ComfortableVote))
    );

    public bool IsWarmerThanTheSummerZone(double airCelsius, double humidityRatioGramsPerKilogram) =>
        Vote(airCelsius, Psychrometrics.RelativeHumidityPercent(airCelsius, humidityRatioGramsPerKilogram, Assumptions.PressureKilopascals),
            Assumptions.SummerClothingClo, Assumptions.StillAirMetresPerSecond) > ComfortableVote;

    public bool IsColderThanTheWinterZone(double airCelsius, double relativeHumidityPercent) =>
        Vote(airCelsius, relativeHumidityPercent, Assumptions.WinterClothingClo, Assumptions.StillAirMetresPerSecond) < -ComfortableVote;

    public bool IsWithinTheHumidityLimit(double humidityRatioGramsPerKilogram) =>
        humidityRatioGramsPerKilogram <= Assumptions.HumidityRatioLimitGramsPerKilogram;

    // Evaporation moves air along its wet-bulb line, so it can reach the zone only from a wet-bulb at or below the zone's hottest.
    public bool IsReachedByDirectEvaporation(double airCelsius, double humidityRatioGramsPerKilogram) =>
        Psychrometrics.WetBulbCelsius(airCelsius, humidityRatioGramsPerKilogram, Assumptions.PressureKilopascals) <= HottestComfortableWetBulbCelsius;

    public bool DirectEvaporationWorks(double airCelsius, double humidityRatioGramsPerKilogram) =>
        IsWithinTheHumidityLimit(humidityRatioGramsPerKilogram) && IsReachedByDirectEvaporation(airCelsius, humidityRatioGramsPerKilogram);

    // The indirect stage cools the air at constant humidity first, which lowers its wet-bulb before the direct stage.
    public bool TwoStageEvaporationWorks(double airCelsius, double humidityRatioGramsPerKilogram)
    {
        var wetBulb = Psychrometrics.WetBulbCelsius(airCelsius, humidityRatioGramsPerKilogram, Assumptions.PressureKilopascals);
        var afterTheIndirectStage = airCelsius - Assumptions.IndirectEvaporativeStageShare * (airCelsius - wetBulb);
        return Psychrometrics.WetBulbCelsius(afterTheIndirectStage, humidityRatioGramsPerKilogram, Assumptions.PressureKilopascals)
               <= HottestComfortableWetBulbCelsius;
    }

    public bool IsInAZone(double airCelsius, double relativeHumidityPercent, double airSpeedMetresPerSecond) =>
        Math.Abs(Vote(airCelsius, relativeHumidityPercent, Assumptions.SummerClothingClo, airSpeedMetresPerSecond)) <= ComfortableVote
        || Math.Abs(Vote(airCelsius, relativeHumidityPercent, Assumptions.WinterClothingClo, airSpeedMetresPerSecond)) <= ComfortableVote;

    private double Vote(double airCelsius, double relativeHumidityPercent, double clothingClo, double airSpeedMetresPerSecond) =>
        comfort.PredictedMeanVote(airCelsius, relativeHumidityPercent, clothingClo, airSpeedMetresPerSecond, Assumptions.MetabolicRateMet, Assumptions.PressureKilopascals);

    private double TemperatureWhereTheVoteReaches(double humidityRatioGramsPerKilogram, double clothingClo, double airSpeedMetresPerSecond, double vote)
    {
        double cooler = CoolestSearchedCelsius, warmer = WarmestSearchedCelsius;
        for (var step = 0; step < 60; step++)
        {
            var middle = (cooler + warmer) / 2;
            var relativeHumidity = Psychrometrics.RelativeHumidityPercent(middle, humidityRatioGramsPerKilogram, Assumptions.PressureKilopascals);
            if (Vote(middle, relativeHumidity, clothingClo, airSpeedMetresPerSecond) < vote) cooler = middle;
            else warmer = middle;
        }
        return (cooler + warmer) / 2;
    }
}
