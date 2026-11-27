namespace vos.Service.Stomata.Services;

public sealed class HourTests(DesignStrategies strategies, double airCelsius, double relativeHumidityPercent)
{
    public const string InsideStillAirZone = "insideStillAirZone";
    public const string InsideMovingAirZone = "insideMovingAirZone";
    public const string InsideAComfortZone = "insideAComfortZone";
    public const string HumidityRatioWithinLimit = "humidityRatioWithinLimit";
    public const string ColderThanWinterZone = "colderThanWinterZone";
    public const string WarmerThanSummerZone = "warmerThanSummerZone";
    public const string ReachedByDirectEvaporation = "reachedByDirectEvaporation";
    public const string ReachedByTwoStageEvaporation = "reachedByTwoStageEvaporation";

    public static readonly IReadOnlyList<string> Names =
    [
        InsideStillAirZone, InsideMovingAirZone, InsideAComfortZone, HumidityRatioWithinLimit,
        ColderThanWinterZone, WarmerThanSummerZone, ReachedByDirectEvaporation, ReachedByTwoStageEvaporation,
    ];

    public bool Holds(string test) => throw new NotImplementedException();
}
