namespace vos.Service.Stomata.Services;

// ASHRAE Handbook, Fundamentals (2017), chapter 1.
public static class Psychrometrics
{
    private const double KelvinAtZeroCelsius = 273.15;
    private const double SeaLevelPressureKilopascals = 101.325;
    private const double WaterToDryAirMolarMassRatio = 0.621945;

    // Equation 5 over ice below 0 °C, equation 6 over liquid water from 0 °C.
    public static double SaturationPressureKilopascals(double celsius)
    {
        var kelvin = celsius + KelvinAtZeroCelsius;
        var logarithmOfPascals = celsius < 0
            ? -5.6745359e3 / kelvin + 6.3925247 - 9.6778430e-3 * kelvin + 6.2215701e-7 * kelvin * kelvin
              + 2.0747825e-9 * Math.Pow(kelvin, 3) - 9.4840240e-13 * Math.Pow(kelvin, 4) + 4.1635019 * Math.Log(kelvin)
            : -5.8002206e3 / kelvin + 1.3914993 - 4.8640239e-2 * kelvin + 4.1764768e-5 * kelvin * kelvin
              - 1.4452093e-8 * Math.Pow(kelvin, 3) + 6.5459673 * Math.Log(kelvin);
        return Math.Exp(logarithmOfPascals) / 1000;
    }

    // Equation 3, the standard atmosphere.
    public static double PressureAtElevationKilopascals(double metres) =>
        SeaLevelPressureKilopascals * Math.Pow(1 - 2.25577e-5 * metres, 5.2559);

    // Equation 20, with the water vapour's pressure the relative humidity's share of saturation (equation 24).
    public static double HumidityRatioGramsPerKilogram(double celsius, double relativeHumidityPercent, double elevationMetres)
    {
        var vapour = relativeHumidityPercent / 100 * SaturationPressureKilopascals(celsius);
        return 1000 * WaterToDryAirMolarMassRatio * vapour / (PressureAtElevationKilopascals(elevationMetres) - vapour);
    }
}
