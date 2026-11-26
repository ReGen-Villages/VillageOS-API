namespace vos.Service.Watershed.Terrain;

public sealed record TerrainSurface(int Columns, int Rows, double CellSizeMetres, IReadOnlyList<double> Heights, IReadOnlyList<bool> Inside);

public sealed record TerrainFigures(
    double AreaHectares, double LowestMetres, double HighestMetres, double MeanMetres,
    double SlopeMeanPercent, double SlopeMedianPercent, double SlopeNinetiethPercentilePercent,
    double HollowHectares, int LowestCell);

public sealed record TerrainAnalysisResult(
    IReadOnlyList<double> SlopePercent, IReadOnlyList<double> AspectDegrees, IReadOnlyList<double> DrainageAreaHectares,
    IReadOnlyList<double> HollowDepthMetres, IReadOnlyList<int> CellsCounted, double CellHectares, TerrainFigures Figures);

public static class TerrainAnalysis
{
    public const double FlatBelowPercent = 1;
    public const double HollowAtLeastMetres = 0.5;
    public const double RiseAcrossAFilledFlatMetres = 1e-3;

    private static readonly (int Column, int Row)[] Neighbours = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    public static TerrainAnalysisResult Of(TerrainSurface surface)
    {
        var (slope, aspect) = SlopeAndAspectByHornsKernel(surface);
        var (drainageCells, hollowDepth) = DrainageByPriorityFloodAndSteepestDescent(surface);
        var cellHectares = surface.CellSizeMetres * surface.CellSizeMetres / 10_000;

        var inside = Enumerable.Range(0, surface.Heights.Count).Where(cell => surface.Inside[cell]).ToArray();
        var counted = inside.Length > 0 ? inside : [.. Enumerable.Range(0, surface.Heights.Count)];

        var heights = counted.Select(cell => surface.Heights[cell]).ToArray();
        var slopes = counted.Select(cell => slope[cell]).Order().ToArray();
        var lowestCell = counted.Aggregate(counted[0], (lowest, cell) => surface.Heights[cell] < surface.Heights[lowest] ? cell : lowest);
        var figures = new TerrainFigures(
            AreaHectares: counted.Length * cellHectares,
            LowestMetres: heights.Min(),
            HighestMetres: heights.Max(),
            MeanMetres: heights.Sum() / heights.Length,
            SlopeMeanPercent: slopes.Sum() / slopes.Length,
            SlopeMedianPercent: slopes[slopes.Length / 2],
            SlopeNinetiethPercentilePercent: slopes[(int)Math.Floor(slopes.Length * 0.9)],
            HollowHectares: counted.Count(cell => hollowDepth[cell] >= HollowAtLeastMetres) * cellHectares,
            LowestCell: lowestCell);

        return new TerrainAnalysisResult(
            slope, aspect, [.. drainageCells.Select(cells => cells * cellHectares)], hollowDepth, counted, cellHectares, figures);
    }

    private static (double[] Slope, double[] Aspect) SlopeAndAspectByHornsKernel(TerrainSurface surface)
    {
        var (columns, rows, cell) = (surface.Columns, surface.Rows, surface.CellSizeMetres);
        double At(int column, int row) =>
            surface.Heights[Math.Clamp(row, 0, rows - 1) * columns + Math.Clamp(column, 0, columns - 1)];

        var slope = new double[columns * rows];
        var aspect = new double[columns * rows];
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var eastward = (At(column + 1, row + 1) + 2 * At(column + 1, row) + At(column + 1, row - 1)
                            - (At(column - 1, row + 1) + 2 * At(column - 1, row) + At(column - 1, row - 1))) / (8 * cell);
            var northward = (At(column - 1, row + 1) + 2 * At(column, row + 1) + At(column + 1, row + 1)
                             - (At(column - 1, row - 1) + 2 * At(column, row - 1) + At(column + 1, row - 1))) / (8 * cell);
            var index = row * columns + column;
            slope[index] = Math.Sqrt(eastward * eastward + northward * northward) * 100;
            aspect[index] = slope[index] < FlatBelowPercent
                ? double.NaN
                : (Math.Atan2(-eastward, -northward) * 180 / Math.PI + 360) % 360;
        }
        return (slope, aspect);
    }

    private static (double[] DrainageCells, double[] HollowDepth) DrainageByPriorityFloodAndSteepestDescent(TerrainSurface surface)
    {
        var (columns, rows, cell) = (surface.Columns, surface.Rows, surface.CellSizeMetres);
        var count = columns * rows;
        var filled = new double[count];
        var done = new bool[count];
        var lowestFirst = new LowestFirst();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            if (column != 0 && row != 0 && column != columns - 1 && row != rows - 1) continue;
            var index = row * columns + column;
            filled[index] = surface.Heights[index];
            done[index] = true;
            lowestFirst.Push(surface.Heights[index], index);
        }
        while (lowestFirst.Count > 0)
        {
            var (height, index) = lowestFirst.Pop();
            var (column, row) = (index % columns, index / columns);
            foreach (var (stepColumn, stepRow) in Neighbours)
            {
                var (nextColumn, nextRow) = (column + stepColumn, row + stepRow);
                if (nextColumn < 0 || nextRow < 0 || nextColumn >= columns || nextRow >= rows) continue;
                var next = nextRow * columns + nextColumn;
                if (done[next]) continue;
                done[next] = true;
                filled[next] = Math.Max(surface.Heights[next], height + RiseAcrossAFilledFlatMetres);
                lowestFirst.Push(filled[next], next);
            }
        }

        var drainsTo = Enumerable.Repeat(-1, count).ToArray();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var index = row * columns + column;
            var steepest = 0.0;
            foreach (var (stepColumn, stepRow) in Neighbours)
            {
                var (nextColumn, nextRow) = (column + stepColumn, row + stepRow);
                if (nextColumn < 0 || nextRow < 0 || nextColumn >= columns || nextRow >= rows) continue;
                var drop = (filled[index] - filled[nextRow * columns + nextColumn]) / (Math.Sqrt(stepColumn * stepColumn + stepRow * stepRow) * cell);
                if (drop <= steepest) continue;
                steepest = drop;
                drainsTo[index] = nextRow * columns + nextColumn;
            }
        }

        var drainageCells = Enumerable.Repeat(1.0, count).ToArray();
        foreach (var index in Enumerable.Range(0, count).OrderByDescending(index => filled[index]))
            if (drainsTo[index] >= 0) drainageCells[drainsTo[index]] += drainageCells[index];

        var hollowDepth = Enumerable.Range(0, count)
            .Select(index => Math.Max(0, filled[index] - surface.Heights[index] - RiseAcrossAFilledFlatMetres)).ToArray();
        return (drainageCells, hollowDepth);
    }

    // A binary heap that settles ties exactly as the site-study pages' own does, so a filled flat fills in the same order.
    private sealed class LowestFirst
    {
        private readonly List<(double Height, int Index)> _held = [];

        public int Count => _held.Count;

        public void Push(double height, int index)
        {
            _held.Add((height, index));
            for (var child = _held.Count - 1; child > 0;)
            {
                var parent = (child - 1) >> 1;
                if (_held[parent].Height <= _held[child].Height) break;
                (_held[parent], _held[child]) = (_held[child], _held[parent]);
                child = parent;
            }
        }

        public (double Height, int Index) Pop()
        {
            var top = _held[0];
            var last = _held[^1];
            _held.RemoveAt(_held.Count - 1);
            if (_held.Count == 0) return top;

            _held[0] = last;
            for (var at = 0; ;)
            {
                var (left, right, lowest) = (2 * at + 1, 2 * at + 2, at);
                if (left < _held.Count && _held[left].Height < _held[lowest].Height) lowest = left;
                if (right < _held.Count && _held[right].Height < _held[lowest].Height) lowest = right;
                if (lowest == at) break;
                (_held[lowest], _held[at]) = (_held[at], _held[lowest]);
                at = lowest;
            }
            return top;
        }
    }
}
