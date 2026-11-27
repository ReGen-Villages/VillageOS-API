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

    private readonly Dictionary<string, bool> _worked = [];
    private double? _humidityRatio;

    // Each test is worked out the first time a group asks for it, so an hour costs only the votes its groups need.
    public bool Holds(string test)
    {
        if (!_worked.TryGetValue(test, out var holds)) _worked[test] = holds = WorkOut(test);
        return holds;
    }

    private bool WorkOut(string test) => test switch
    {
        InsideStillAirZone => strategies.IsInAZone(airCelsius, relativeHumidityPercent, strategies.Assumptions.StillAirMetresPerSecond),
        InsideMovingAirZone => strategies.IsInAZone(airCelsius, relativeHumidityPercent, strategies.Assumptions.AirMovementMetresPerSecond),
        InsideAComfortZone => Holds(InsideStillAirZone) || Holds(InsideMovingAirZone),
        HumidityRatioWithinLimit => strategies.IsWithinTheHumidityLimit(HumidityRatio),
        ColderThanWinterZone => strategies.IsColderThanTheWinterZone(airCelsius, relativeHumidityPercent),
        WarmerThanSummerZone => strategies.IsWarmerThanTheSummerZone(airCelsius, HumidityRatio),
        ReachedByDirectEvaporation => strategies.IsReachedByDirectEvaporation(airCelsius, HumidityRatio),
        ReachedByTwoStageEvaporation => strategies.TwoStageEvaporationWorks(airCelsius, HumidityRatio),
        _ => throw new ArgumentOutOfRangeException(nameof(test), test, "Stomata knows no such test."),
    };

    private double HumidityRatio => _humidityRatio ??=
        Psychrometrics.HumidityRatioAtPressureGramsPerKilogram(airCelsius, relativeHumidityPercent, strategies.Assumptions.PressureKilopascals);
}
