namespace vos.Service.Watershed.Terrain;

// Points run row by row from the south-west, west to east, as a stored grid's values do.
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

    public IReadOnlyList<Position> Points => throw new NotImplementedException();

    public static HeightGridLayout Over(IReadOnlyList<Position> border) => throw new NotImplementedException();
}
