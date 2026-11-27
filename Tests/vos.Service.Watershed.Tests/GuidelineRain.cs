using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Tests;

// The two storms of the reference parcel's rain table in the stormwater guideline.
public static class GuidelineRain
{
    public static readonly double[] Durations = [1440, 720, 360, 300, 240, 180, 120, 90, 60, 45, 30, 20, 15, 10, 5];
    public static readonly double[] OneInFive = [1.93, 3.68, 6.22, 7.21, 8.62, 10.81, 15.11, 18.66, 25.35, 31.78, 41.68, 53.54, 63.34, 76.98, 97.41];
    public static readonly double[] OneInHundred = [6.1, 11.44, 18.12, 20.66, 24.61, 31.06, 44.32, 53.52, 72.81, 90.83, 111.63, 134.49, 155.29, 188.11, 209.2];

    public static RainfallIntensities Table(double[] millimetresPerHour) =>
        new(Durations.Zip(millimetresPerHour, (minutes, rain) => new RainfallIntensity(minutes, rain)));
}
