namespace vos.Service.Watershed.Terrain;

public sealed record HollowRidge(int Basin, int OtherBasin, double Height);

public sealed record Hollow(
    IReadOnlyList<int> Cells, IReadOnlyList<int> BasinOfCell, int Basins, IReadOnlyList<HollowRidge> Ridges,
    int PourPoint, IReadOnlyList<int> Exits, double SpillLevel);

public sealed record HollowFill(IReadOnlyList<double> StandingDepths, double Overflow);

public static class Hollows
{
    private static readonly (int Column, int Row)[] Neighbours = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    // Two raised cells that touch always belong to one hollow: had their spill levels differed, the levelling would have
    // reached the higher one from the lower one first and raised it into the lower one's hollow.
    public static IReadOnlyList<Hollow> Of(TerrainSurface surface, TerrainAnalysisResult analysed)
    {
        var raised = analysed.HollowDepthMetres.Select(depth => depth > 0).ToArray();
        var seen = new bool[raised.Length];
        var hollows = new List<Hollow>();
        for (var start = 0; start < raised.Length; start++)
        {
            if (!raised[start] || seen[start]) continue;
            seen[start] = true;
            List<int> cells = [start];
            for (var next = 0; next < cells.Count; next++)
                foreach (var neighbour in NeighboursOf(surface, cells[next]).Where(neighbour => raised[neighbour] && !seen[neighbour]))
                {
                    seen[neighbour] = true;
                    cells.Add(neighbour);
                }
            hollows.Add(HollowOf(surface, analysed, cells));
        }
        return hollows;
    }

    public static IReadOnlyList<IReadOnlyList<double>> AreaEnteringEachBasin(
        IReadOnlyList<Hollow> hollows, IReadOnlyList<int> drainsTo, IReadOnlyList<double> gatheredArea, double cellArea)
    {
        var placeOf = new Dictionary<int, (int Hollow, int Basin)>();
        var entering = hollows.Select(hollow => new double[hollow.Basins]).ToArray();
        for (var number = 0; number < hollows.Count; number++)
            for (var index = 0; index < hollows[number].Cells.Count; index++)
            {
                placeOf[hollows[number].Cells[index]] = (number, hollows[number].BasinOfCell[index]);
                entering[number][hollows[number].BasinOfCell[index]] += cellArea;
            }
        for (var cell = 0; cell < drainsTo.Count; cell++)
            if (drainsTo[cell] is var next and >= 0 && placeOf.TryGetValue(next, out var into)
                && !(placeOf.TryGetValue(cell, out var from) && from.Hollow == into.Hollow))
                entering[into.Hollow][into.Basin] += gatheredArea[cell];
        return entering;
    }

    private static Hollow HollowOf(TerrainSurface surface, TerrainAnalysisResult analysed, List<int> cells)
    {
        var heights = surface.Heights;
        var position = cells.Select((cell, index) => (cell, index)).ToDictionary(pair => pair.cell, pair => pair.index);
        var pourPoint = cells.SelectMany(cell => NeighboursOf(surface, cell)).Where(neighbour => !position.ContainsKey(neighbour))
            .MinBy(neighbour => analysed.FilledHeights[neighbour]);

        var basinOf = Enumerable.Repeat(-1, cells.Count).ToArray();
        var lowestFirst = new PriorityQueue<int, double>();
        var basins = 0;
        foreach (var (cell, index) in position)
        {
            if (NeighboursOf(surface, cell).Any(neighbour => position.ContainsKey(neighbour) && heights[neighbour] < heights[cell])) continue;
            basinOf[index] = basins++;
            lowestFirst.Enqueue(index, heights[cell]);
        }
        var ridges = new Dictionary<(int, int), double>();
        while (lowestFirst.TryDequeue(out var index, out _))
            foreach (var neighbour in NeighboursOf(surface, cells[index]).Where(position.ContainsKey).Select(neighbour => position[neighbour]))
            {
                if (basinOf[neighbour] < 0)
                {
                    basinOf[neighbour] = basinOf[index];
                    lowestFirst.Enqueue(neighbour, heights[cells[neighbour]]);
                    continue;
                }
                if (basinOf[neighbour] == basinOf[index]) continue;
                var pair = (Math.Min(basinOf[index], basinOf[neighbour]), Math.Max(basinOf[index], basinOf[neighbour]));
                var height = Math.Max(heights[cells[index]], heights[cells[neighbour]]);
                ridges[pair] = ridges.TryGetValue(pair, out var lowest) ? Math.Min(lowest, height) : height;
            }

        return new Hollow(
            cells, basinOf, basins, [.. ridges.Select(ridge => new HollowRidge(ridge.Key.Item1, ridge.Key.Item2, ridge.Value))],
            pourPoint, [.. cells.Where(cell => analysed.DrainsTo[cell] >= 0 && !position.ContainsKey(analysed.DrainsTo[cell]))],
            analysed.FilledHeights[pourPoint]);
    }

    private static IEnumerable<int> NeighboursOf(TerrainSurface surface, int cell)
    {
        var (column, row) = (cell % surface.Columns, cell / surface.Columns);
        foreach (var (stepColumn, stepRow) in Neighbours)
        {
            var (nextColumn, nextRow) = (column + stepColumn, row + stepRow);
            if (nextColumn >= 0 && nextRow >= 0 && nextColumn < surface.Columns && nextRow < surface.Rows)
                yield return nextRow * surface.Columns + nextColumn;
        }
    }
}

// Each pit fills from its own inflow until it reaches the ridge to its neighbour, spills into the basin across that
// ridge, and the two share one level only once both are full to it.
public static class HollowWater
{
    private sealed class Pool(List<int> cells, int leaf)
    {
        public List<int> Cells { get; } = cells;
        public int Leaf { get; } = leaf;
        public Pool? First { get; init; }
        public Pool? Second { get; init; }
        public HollowRidge? Ridge { get; init; }
        public double SpillsAt { get; set; }
        public double Held { get; set; }
        public bool RisesAboveItsRidge { get; set; }
    }

    public static HollowFill Fill(
        IReadOnlyList<double> heights, IReadOnlyList<int> basinOfCell, IReadOnlyList<HollowRidge> ridges,
        IReadOnlyList<double> inflowOfBasin, double spillLevel, double cellArea)
    {
        var basins = inflowOfBasin.Count;
        var pools = Enumerable.Range(0, basins)
            .Select(basin => new Pool([.. Enumerable.Range(0, heights.Count).Where(cell => basinOfCell[cell] == basin)], basin)).ToArray();
        var topOf = pools.ToArray();
        var setOf = Enumerable.Range(0, basins).ToArray();
        int Find(int basin) => setOf[basin] == basin ? basin : setOf[basin] = Find(setOf[basin]);
        foreach (var ridge in ridges.OrderBy(ridge => ridge.Height))
        {
            var (first, second) = (Find(ridge.Basin), Find(ridge.OtherBasin));
            if (first == second) continue;
            topOf[first].SpillsAt = topOf[second].SpillsAt = ridge.Height;
            setOf[second] = first;
            topOf[first] = new Pool([.. topOf[first].Cells, .. topOf[second].Cells], -1) { First = topOf[first], Second = topOf[second], Ridge = ridge };
        }
        var roots = Enumerable.Range(0, basins).Where(basin => Find(basin) == basin).Select(basin => topOf[basin]).ToList();
        foreach (var root in roots) root.SpillsAt = spillLevel;

        var inflow = inflowOfBasin.ToArray();
        double Store(Pool pool, double level) => pool.Cells.Sum(cell => Math.Max(0, level - heights[cell])) * cellArea;

        double Settle(Pool pool)
        {
            if (pool.Ridge is not { } ridge)
            {
                pool.Held = Math.Min(inflow[pool.Leaf], Store(pool, pool.SpillsAt));
                return inflow[pool.Leaf] - pool.Held;
            }
            var (first, second) = (pool.First!, pool.Second!);
            var (fromFirst, fromSecond) = (Settle(first), Settle(second));
            var (intoFirst, intoSecond) = (ridge.Basin, ridge.OtherBasin);
            if (fromFirst > 0 && fromSecond == 0)
            {
                inflow[intoSecond] += fromFirst;
                (fromFirst, fromSecond) = (0, Settle(second));
            }
            else if (fromSecond > 0 && fromFirst == 0)
            {
                inflow[intoFirst] += fromSecond;
                (fromFirst, fromSecond) = (Settle(first), 0);
            }
            var above = fromFirst + fromSecond;
            pool.RisesAboveItsRidge = above > 0;
            var total = first.Held + second.Held + above;
            pool.Held = Math.Min(total, Store(pool, pool.SpillsAt));
            return total - pool.Held;
        }

        var overflow = roots.Sum(Settle);
        var depths = new double[heights.Count];
        void Stand(Pool pool)
        {
            if (pool.Ridge is null || pool.RisesAboveItsRidge)
            {
                var level = LevelHolding(pool.Cells.Select(cell => heights[cell]), pool.Held / cellArea);
                foreach (var cell in pool.Cells) depths[cell] = Math.Max(0, level - heights[cell]);
                return;
            }
            Stand(pool.First!);
            Stand(pool.Second!);
        }
        foreach (var root in roots) Stand(root);
        return new HollowFill(depths, overflow);
    }

    public static IReadOnlyList<double> InflowOfEachBasin(IReadOnlyList<double> areaEnteringEachBasin, double gathered)
    {
        var total = areaEnteringEachBasin.Sum();
        return [.. areaEnteringEachBasin.Select(area => gathered * area / total)];
    }

    private static double LevelHolding(IEnumerable<double> heights, double depthSum)
    {
        var sorted = heights.Order().ToArray();
        var below = sorted[0];
        var count = 1;
        while (count < sorted.Length && (depthSum + below) / count > sorted[count]) below += sorted[count++];
        return (depthSum + below) / count;
    }
}
