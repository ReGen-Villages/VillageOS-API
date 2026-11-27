namespace vos.Service.Watershed.Terrain;

public sealed class HeightGridLayout
{
    public const double MarginOverTheBox = 1.3;
    public const double ShortestSideMetres = 400;
    public const double SmallestCellMetres = 30;
    public const int MostPointsAlongASide = 28;

    private HeightGridLayout(
        int columns, int rows, double cellSizeMetres, double originLatitude, double originLongitude,
        double latitudeStepDegrees, double longitudeStepDegrees, bool[] inside)
    {
        Columns = columns;
        Rows = rows;
        CellSizeMetres = cellSizeMetres;
        OriginLatitude = originLatitude;
        OriginLongitude = originLongitude;
        LatitudeStepDegrees = latitudeStepDegrees;
        LongitudeStepDegrees = longitudeStepDegrees;
        Inside = inside;
    }

    public int Columns { get; }
    public int Rows { get; }
    public double CellSizeMetres { get; }
    public double OriginLatitude { get; }
    public double OriginLongitude { get; }
    public double LatitudeStepDegrees { get; }
    public double LongitudeStepDegrees { get; }
    public IReadOnlyList<bool> Inside { get; }

    public IReadOnlyList<Position> PointsRowByRowFromTheSouthWest =>
    [
        .. Enumerable.Range(0, Rows * Columns).Select(cell => new Position(
            OriginLatitude + cell / Columns * LatitudeStepDegrees, OriginLongitude + cell % Columns * LongitudeStepDegrees)),
    ];

    public static HeightGridLayout Over(IReadOnlyList<Position> border)
    {
        var (south, north) = (border.Min(corner => corner.Latitude), border.Max(corner => corner.Latitude));
        var (west, east) = (border.Min(corner => corner.Longitude), border.Max(corner => corner.Longitude));
        var middle = new Position((south + north) / 2, (west + east) / 2);
        var metresPerDegreeOfLongitude = EarthDistances.MetresPerDegreeOfLongitudeAt(middle.Latitude);

        var heightMetres = Math.Max((north - south) * EarthDistances.MetresPerDegreeOfLatitude * MarginOverTheBox, ShortestSideMetres);
        var widthMetres = Math.Max((east - west) * metresPerDegreeOfLongitude * MarginOverTheBox, ShortestSideMetres);
        var cell = Math.Max(SmallestCellMetres, Math.Max(widthMetres, heightMetres) / (MostPointsAlongASide - 1));
        var columns = Math.Min(MostPointsAlongASide, (int)Math.Round(widthMetres / cell, MidpointRounding.AwayFromZero) + 1);
        var rows = Math.Min(MostPointsAlongASide, (int)Math.Round(heightMetres / cell, MidpointRounding.AwayFromZero) + 1);

        var latitudeStep = cell / EarthDistances.MetresPerDegreeOfLatitude;
        var longitudeStep = cell / metresPerDegreeOfLongitude;
        var originLatitude = middle.Latitude - (rows - 1) / 2.0 * latitudeStep;
        var originLongitude = middle.Longitude - (columns - 1) / 2.0 * longitudeStep;

        var outline = border.Select(corner => (
            Across: (corner.Longitude - originLongitude) / longitudeStep, Up: (corner.Latitude - originLatitude) / latitudeStep)).ToArray();
        var inside = Enumerable.Range(0, rows * columns).Select(cell => Crossings.Encloses(outline, cell % columns, cell / columns)).ToArray();

        return new HeightGridLayout(columns, rows, cell, originLatitude, originLongitude, latitudeStep, longitudeStep, inside);
    }
}
