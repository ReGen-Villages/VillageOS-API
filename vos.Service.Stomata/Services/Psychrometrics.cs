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

    public static double HumidityRatioGramsPerKilogram(double celsius, double relativeHumidityPercent, double elevationMetres) =>
        HumidityRatioAtPressureGramsPerKilogram(celsius, relativeHumidityPercent, PressureAtElevationKilopascals(elevationMetres));

    // Equation 20, with the water vapour's pressure the relative humidity's share of saturation (equation 24).
    public static double HumidityRatioAtPressureGramsPerKilogram(double celsius, double relativeHumidityPercent, double pressureKilopascals)
    {
        var vapour = relativeHumidityPercent / 100 * SaturationPressureKilopascals(celsius);
        return 1000 * WaterToDryAirMolarMassRatio * vapour / (pressureKilopascals - vapour);
    }

    public static double RelativeHumidityPercent(double celsius, double humidityRatioGramsPerKilogram, double pressureKilopascals)
    {
        var ratio = humidityRatioGramsPerKilogram / 1000;
        var vapour = ratio * pressureKilopascals / (WaterToDryAirMolarMassRatio + ratio);
        return Math.Min(100, 100 * vapour / SaturationPressureKilopascals(celsius));
    }

    // Equation 33 for a wet bulb above freezing and equation 35 below it, solved by halving the interval.
    public static double WetBulbCelsius(double celsius, double humidityRatioGramsPerKilogram, double pressureKilopascals)
    {
        var target = humidityRatioGramsPerKilogram / 1000;
        if (HumidityRatioAtWetBulb(celsius, celsius, pressureKilopascals) <= target) return celsius;

        double cooler = -100, warmer = celsius;
        for (var step = 0; step < 60; step++)
        {
            var middle = (cooler + warmer) / 2;
            if (HumidityRatioAtWetBulb(celsius, middle, pressureKilopascals) < target) cooler = middle;
            else warmer = middle;
        }
        return (cooler + warmer) / 2;
    }

    private static double HumidityRatioAtWetBulb(double celsius, double wetBulb, double pressureKilopascals)
    {
        var saturation = SaturationPressureKilopascals(wetBulb);
        var saturated = WaterToDryAirMolarMassRatio * saturation / (pressureKilopascals - saturation);
        return wetBulb >= 0
            ? ((2501 - 2.326 * wetBulb) * saturated - 1.006 * (celsius - wetBulb)) / (2501 + 1.86 * celsius - 4.186 * wetBulb)
            : ((2830 - 0.24 * wetBulb) * saturated - 1.006 * (celsius - wetBulb)) / (2830 + 1.86 * celsius - 2.1 * wetBulb);
    }
}
