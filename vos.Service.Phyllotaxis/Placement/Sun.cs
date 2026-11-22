namespace vos.Service.Phyllotaxis.Placement;

public readonly record struct SunSample(int DayOfYear, int Hour, double ElevationRadians, double AzimuthRadians, double ElevationSine);

// SunShare is the share of the sun that level open ground gets; ShadeShare the share of the sun on the ground lost to the land's shade.
public sealed record SunAndShade(IReadOnlyList<double> SunShare, IReadOnlyList<double> ShadeShare);

// The sun's path by declination and hour angle (Cooper, 1969) on the equinoxes and solstices, in solar time.
public static class Sun
{
    private const double AxialTiltDegrees = 23.44;
    private const double DaysInYear = 365;
    private const int DeclinationDayOffset = 284;
    private const int SolarNoonHour = 12;
    private const double DegreesPerHour = 15;
    private static readonly int[] SampleDays = [80, 172, 266, 355];

    public static double DeclinationDegrees(int dayOfYear)
    {
        throw new NotImplementedException();
    }

    public static IReadOnlyList<SunSample> Samples(double latitudeDegrees, SunAssumptions assumptions)
    {
        throw new NotImplementedException();
    }

    public static SunAndShade OnTerrain(TerrainGrid grid, double latitudeDegrees, SunAssumptions assumptions)
    {
        throw new NotImplementedException();
    }
}
