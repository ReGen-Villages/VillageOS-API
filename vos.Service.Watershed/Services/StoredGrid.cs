using System.Text.Json;
using vos.Service.Shared.Subscriptions;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed record StoredGrid(
    double OriginLatitude, double OriginLongitude, double CellSizeMetres, double LatitudeStepDegrees, double LongitudeStepDegrees,
    int Rows, int Columns, IReadOnlyList<double> Values)
{
    private const double SamePosition = 1e-12;
    private const double SameLength = 1e-9;

    public static StoredGrid? Read(SnapshotThing thing, string property)
    {
        if (thing.StatedValue(property) is not { Value: { ValueKind: JsonValueKind.Object } held }) return null;
        try
        {
            return new StoredGrid(
                held.GetProperty("originLatitude").GetDouble(), held.GetProperty("originLongitude").GetDouble(),
                held.GetProperty("cellSizeMetres").GetDouble(), held.GetProperty("latitudeStepDegrees").GetDouble(),
                held.GetProperty("longitudeStepDegrees").GetDouble(), held.GetProperty("rows").GetInt32(), held.GetProperty("columns").GetInt32(),
                [.. held.GetProperty("values").EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.NaN)]);
        }
        catch (Exception unreadable) when (unreadable is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public bool LaidOutAs(HeightGridLayout layout) =>
        Laid(layout.OriginLatitude, layout.OriginLongitude, layout.CellSizeMetres, layout.LatitudeStepDegrees, layout.LongitudeStepDegrees,
            layout.Rows, layout.Columns);

    public bool LaidOutAs(ProfileLine line, int rows) =>
        Laid(line.Points[0].Latitude, line.Points[0].Longitude, line.SpacingMetres, line.LatitudeStepDegrees, line.LongitudeStepDegrees,
            rows, line.Points.Count / rows);

    public static object Written(HeightGridLayout layout, IReadOnlyList<double> values) => Written(
        layout.OriginLatitude, layout.OriginLongitude, layout.CellSizeMetres, layout.LatitudeStepDegrees, layout.LongitudeStepDegrees,
        layout.Rows, layout.Columns, values);

    public static object Written(ProfileLine line, int rows, IReadOnlyList<double> values) => Written(
        line.Points[0].Latitude, line.Points[0].Longitude, line.SpacingMetres, line.LatitudeStepDegrees, line.LongitudeStepDegrees,
        rows, line.Points.Count / rows, values);

    private bool Laid(
        double originLatitude, double originLongitude, double cellSizeMetres, double latitudeStepDegrees, double longitudeStepDegrees,
        int rows, int columns) =>
        Rows == rows && Columns == columns && Values.Count == rows * columns
        && Math.Abs(OriginLatitude - originLatitude) < SamePosition && Math.Abs(OriginLongitude - originLongitude) < SamePosition
        && Math.Abs(LatitudeStepDegrees - latitudeStepDegrees) < SamePosition && Math.Abs(LongitudeStepDegrees - longitudeStepDegrees) < SamePosition
        && Math.Abs(CellSizeMetres - cellSizeMetres) < SameLength;

    // The model keeps a grid's values at single precision, so they are written as the value it will hold.
    private static object Written(
        double originLatitude, double originLongitude, double cellSizeMetres, double latitudeStepDegrees, double longitudeStepDegrees,
        int rows, int columns, IReadOnlyList<double> values) => new
        {
            originLatitude, originLongitude, cellSizeMetres, latitudeStepDegrees, longitudeStepDegrees, rows, columns,
            values = values.Select(value => double.IsNaN(value) ? (double?)null : (double)(float)value).ToArray(),
        };
}
