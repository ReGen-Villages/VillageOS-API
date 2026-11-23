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

    public static double DeclinationDegrees(int dayOfYear) =>
        AxialTiltDegrees * Math.Sin(Radians(360 / DaysInYear * (DeclinationDayOffset + dayOfYear)));

    public static IReadOnlyList<SunSample> Samples(double latitudeDegrees, SunAssumptions assumptions)
    {
        var latitude = Radians(latitudeDegrees);
        var samples = new List<SunSample>();
        foreach (var day in SampleDays)
        {
            var declination = Radians(DeclinationDegrees(day));
            for (var hour = assumptions.FirstHour; hour <= assumptions.LastHour; hour++)
            {
                var hourAngle = Radians((hour - SolarNoonHour) * DegreesPerHour);
                var elevationSine = Math.Sin(latitude) * Math.Sin(declination) + Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle);
                if (elevationSine < assumptions.LowestElevationSine) continue;

                var azimuth = Math.Atan2(
                    -Math.Cos(declination) * Math.Sin(hourAngle),
                    Math.Sin(declination) * Math.Cos(latitude) - Math.Cos(declination) * Math.Sin(latitude) * Math.Cos(hourAngle));
                samples.Add(new SunSample(day, hour, Math.Asin(elevationSine), azimuth, elevationSine));
            }
        }

        return samples;
    }

    public static SunAndShade OnTerrain(TerrainGrid grid, double latitudeDegrees, SunAssumptions assumptions)
    {
        var samples = Samples(latitudeDegrees, assumptions);
        var levelGround = samples.Sum(sample => sample.ElevationSine);
        if (levelGround == 0) levelGround = 1;

        var sunShare = new double[grid.CellCount];
        var shadeShare = new double[grid.CellCount];
        for (var index = 0; index < grid.CellCount; index++)
        {
            if (!grid.Inside[index]) continue;
            var cell = grid.CellAt(index);
            var (normalEast, normalNorth, normalUp) = GroundNormal(grid, cell);

            double reaching = 0, lit = 0;
            foreach (var sample in samples)
            {
                var towardsSunEast = Math.Sin(sample.AzimuthRadians) * Math.Cos(sample.ElevationRadians);
                var towardsSunNorth = Math.Cos(sample.AzimuthRadians) * Math.Cos(sample.ElevationRadians);
                var onTheGround = Math.Max(0, normalEast * towardsSunEast + normalNorth * towardsSunNorth + normalUp * sample.ElevationSine);
                reaching += onTheGround;
                if (HorizonTowards(grid, cell, sample.AzimuthRadians, assumptions.ShadeSearchCells) < sample.ElevationRadians)
                    lit += onTheGround;
            }

            sunShare[index] = lit / levelGround;
            shadeShare[index] = reaching > 0 ? 1 - lit / reaching : 0;
        }

        return new SunAndShade(sunShare, shadeShare);
    }

    private static (double East, double North, double Up) GroundNormal(TerrainGrid grid, Cell cell)
    {
        var riseEastward = (HeightNear(grid, cell.Column + 1, cell.Row) - HeightNear(grid, cell.Column - 1, cell.Row)) / (2 * grid.CellMetres);
        var riseNorthward = (HeightNear(grid, cell.Column, cell.Row + 1) - HeightNear(grid, cell.Column, cell.Row - 1)) / (2 * grid.CellMetres);
        var length = Math.Sqrt(riseEastward * riseEastward + riseNorthward * riseNorthward + 1);
        return (-riseEastward / length, -riseNorthward / length, 1 / length);
    }

    private static double HorizonTowards(TerrainGrid grid, Cell cell, double azimuthRadians, int searchCells)
    {
        var here = grid.HeightMetres[grid.IndexOf(cell)];
        var horizon = 0.0;
        for (var step = 1; step <= searchCells; step++)
        {
            var column = cell.Column + Math.Sin(azimuthRadians) * step;
            var row = cell.Row + Math.Cos(azimuthRadians) * step;
            if (column < 0 || row < 0 || column > grid.Columns - 1 || row > grid.Rows - 1) break;
            horizon = Math.Max(horizon, Math.Atan((HeightNear(grid, column, row) - here) / (step * grid.CellMetres)));
        }

        return horizon;
    }

    private static double HeightNear(TerrainGrid grid, double column, double row)
    {
        var nearest = new Cell(
            Math.Clamp(TerrainGrid.RoundHalfUp(column), 0, grid.Columns - 1),
            Math.Clamp(TerrainGrid.RoundHalfUp(row), 0, grid.Rows - 1));
        return grid.HeightMetres[grid.IndexOf(nearest)];
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
