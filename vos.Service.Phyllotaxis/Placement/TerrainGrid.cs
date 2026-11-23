namespace vos.Service.Phyllotaxis.Placement;

public enum Ground
{
    Buildable,
    Unstable,
    ProtectedZone,
    Floods,
}

public readonly record struct Cell(int Column, int Row);

// Metres east and north of the grid's south-west cell centre.
public readonly record struct Point(double East, double North)
{
    public double DistanceTo(Point other) => Math.Sqrt(Square(East - other.East) + Square(North - other.North));

    private static double Square(double value) => value * value;
}

// Row 0 is the southern row and column 0 the western column; each list holds one value per cell, row by row.
public sealed class TerrainGrid
{
    public TerrainGrid(
        int columns,
        int rows,
        double cellMetres,
        IReadOnlyList<double> heightMetres,
        IReadOnlyList<double> slopePercent,
        IReadOnlyList<double> drainageCells,
        IReadOnlyList<bool> inside,
        IReadOnlyList<Ground> ground)
    {
        var cells = columns * rows;
        RefuseUnlessOnePerCell(heightMetres.Count, cells, nameof(heightMetres));
        RefuseUnlessOnePerCell(slopePercent.Count, cells, nameof(slopePercent));
        RefuseUnlessOnePerCell(drainageCells.Count, cells, nameof(drainageCells));
        RefuseUnlessOnePerCell(inside.Count, cells, nameof(inside));
        RefuseUnlessOnePerCell(ground.Count, cells, nameof(ground));

        Columns = columns;
        Rows = rows;
        CellMetres = cellMetres;
        HeightMetres = heightMetres;
        SlopePercent = slopePercent;
        DrainageCells = drainageCells;
        Inside = inside;
        Ground = ground;
    }

    public int Columns { get; }
    public int Rows { get; }
    public double CellMetres { get; }
    public IReadOnlyList<double> HeightMetres { get; }
    public IReadOnlyList<double> SlopePercent { get; }
    public IReadOnlyList<double> DrainageCells { get; }
    public IReadOnlyList<bool> Inside { get; }
    public IReadOnlyList<Ground> Ground { get; }

    public int CellCount => Columns * Rows;

    public int IndexOf(Cell cell) => cell.Row * Columns + cell.Column;

    public Cell CellAt(int index) => new(index % Columns, index / Columns);

    public bool Holds(Cell cell) => cell.Column >= 0 && cell.Row >= 0 && cell.Column < Columns && cell.Row < Rows;

    public bool IsInside(Cell cell) => Holds(cell) && Inside[IndexOf(cell)];

    public Point CentreOf(Cell cell) => new(cell.Column * CellMetres, cell.Row * CellMetres);

    public Cell NearestCell(Point point) =>
        new(Math.Clamp(RoundHalfUp(point.East / CellMetres), 0, Columns - 1), Math.Clamp(RoundHalfUp(point.North / CellMetres), 0, Rows - 1));

    public double LargestDrainageInside() =>
        Enumerable.Range(0, CellCount).Where(index => Inside[index]).Select(index => DrainageCells[index]).DefaultIfEmpty(0).Max();

    // Logarithmic, so a cell that drains a small share of the site still scores well above nothing.
    internal static double DrainageShare(double drainageCells, double largestDrainage) =>
        largestDrainage > 1 ? Math.Log(Math.Max(1, drainageCells)) / Math.Log(largestDrainage) : 0;

    // The demo rounds as JavaScript's Math.round does, half towards positive infinity, not to the even neighbour.
    internal static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);

    private static void RefuseUnlessOnePerCell(int count, int cells, string name)
    {
        if (count != cells)
            throw new ArgumentException($"{name} holds {count} values for a grid of {cells} cells", name);
    }
}
