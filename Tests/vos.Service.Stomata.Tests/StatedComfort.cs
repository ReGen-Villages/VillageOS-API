using vos.Service.Stomata.Services;

namespace vos.Service.Stomata.Tests;

// A comfort model simple enough to work by hand: neutral at 30 − 8 × clothing degrees in still air, three degrees warmer
// at 1 m/s, a vote of one for every four degrees away, and no effect of humidity. With 0.5 and 1.0 clo the still-air zones
// are 24–28 °C and 20–24 °C, and at 1 m/s 27–31 °C and 23–27 °C.
public sealed class StatedComfort : IThermalComfort
{
    public double PredictedMeanVote(double airCelsius, double relativeHumidityPercent, double clothingClo, double airSpeedMetresPerSecond) =>
        (airCelsius - (30 - 8 * clothingClo + 3 * (airSpeedMetresPerSecond - 0.1) / 0.9)) / 4;

    public static readonly ComfortAssumptions Assumptions = new(
        SummerClothingClo: 0.5, WinterClothingClo: 1.0, StillAirMetresPerSecond: 0.1, AirMovementMetresPerSecond: 1.0,
        HumidityRatioLimitGramsPerKilogram: 12.0, IndirectEvaporativeStageShare: 0.6, PressureKilopascals: 101.325);

    public static DesignStrategies Strategies() => new(new StatedComfort(), Assumptions);
}
