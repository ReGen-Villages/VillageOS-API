namespace vos.Service.Watershed.SiteLayers;

public readonly record struct PlanePoint(double X, double Y);

public sealed record PlanePolygon(IReadOnlyList<PlanePoint> Outer, IReadOnlyList<IReadOnlyList<PlanePoint>> Holes);

public sealed record PlaneLine(IReadOnlyList<PlanePoint> Points);

// An overlap's area is the shoelace sum of the pieces of each outline lying inside the other; holes run clockwise, so they subtract.
public sealed class PlaneShape
{
    private const double RelativeTolerance = 1e-9;

    private readonly PlanePoint[][] _rings;
    private readonly double _extent;

    public PlaneShape(IReadOnlyList<PlanePolygon> polygons)
    {
        _rings = [.. polygons.SelectMany(polygon => polygon.Holes.Select(hole => Turned(hole, anticlockwise: false)).Prepend(Turned(polygon.Outer, anticlockwise: true)))];
        _extent = _rings.SelectMany(ring => ring).Select(point => Math.Max(Math.Abs(point.X), Math.Abs(point.Y))).DefaultIfEmpty(0).Max();
        AreaSquareMetres = _rings.Sum(SignedArea);
    }

    public double AreaSquareMetres { get; }

    // A shared edge counts once, and only where both insides lie on the same side of it.
    public double OverlapSquareMetres(PlaneShape other)
    {
        var tolerance = ToleranceWith(other);
        return OutlineInside(this, other, countsSharedEdges: true, tolerance) + OutlineInside(other, this, countsSharedEdges: false, tolerance);
    }

    public double OutlineDistanceMetres(PlaneShape other) =>
        Math.Min(Nearest(Corners, other.Edges), Nearest(other.Corners, Edges));

    public double LengthInsideMetres(PlaneLine line)
    {
        var tolerance = ToleranceWith(line);
        return Segments(line).Sum(segment => Pieces(segment.From, segment.To, _rings, tolerance)
            .Where(piece => !OnOutline(Middle(piece.From, piece.To), tolerance, out _) && Encloses(Middle(piece.From, piece.To)))
            .Sum(piece => Distance(piece.From, piece.To)));
    }

    public bool IsCrossedBy(PlaneLine line)
    {
        var tolerance = ToleranceWith(line);
        return Segments(line).Any(segment => Edges.Any(edge =>
            Side(segment.From, segment.To, edge.From, tolerance) * Side(segment.From, segment.To, edge.To, tolerance) < 0
            && Side(edge.From, edge.To, segment.From, tolerance) * Side(edge.From, edge.To, segment.To, tolerance) < 0));
    }

    public double DistanceMetres(PlaneLine line) =>
        Math.Min(Nearest(line.Points, Edges), Nearest(Corners, Segments(line)));

    private IEnumerable<PlanePoint> Corners => _rings.SelectMany(ring => ring);

    private IEnumerable<(PlanePoint From, PlanePoint To)> Edges =>
        _rings.SelectMany(ring => ring.Select((corner, index) => (corner, ring[(index + 1) % ring.Length])));

    private static IEnumerable<(PlanePoint From, PlanePoint To)> Segments(PlaneLine line) =>
        line.Points.Zip(line.Points.Skip(1));

    private double ToleranceWith(PlaneShape other) => RelativeTolerance * Math.Max(1, Math.Max(_extent, other._extent));

    private double ToleranceWith(PlaneLine line) =>
        RelativeTolerance * Math.Max(1, Math.Max(_extent, line.Points.Select(point => Math.Max(Math.Abs(point.X), Math.Abs(point.Y))).DefaultIfEmpty(0).Max()));

    private static double OutlineInside(PlaneShape shape, PlaneShape other, bool countsSharedEdges, double tolerance) =>
        shape.Edges.Sum(edge => Pieces(edge.From, edge.To, other._rings, tolerance)
            .Where(piece => other.OnOutline(Middle(piece.From, piece.To), tolerance, out var along)
                ? countsSharedEdges && Dot(piece.From, piece.To, along.From, along.To) > 0
                : other.Encloses(Middle(piece.From, piece.To)))
            .Sum(piece => (piece.From.X * piece.To.Y - piece.To.X * piece.From.Y) / 2));

    private static IEnumerable<(PlanePoint From, PlanePoint To)> Pieces(PlanePoint from, PlanePoint to, PlanePoint[][] cutters, double tolerance)
    {
        List<double> cuts = [0, 1];
        foreach (var ring in cutters)
            for (var index = 0; index < ring.Length; index++)
            {
                var corner = ring[index];
                if (DistanceToSegment(corner, from, to) <= tolerance)
                    cuts.Add(Math.Clamp(Along(corner, from, to), 0, 1));
                if (Crossing(from, to, corner, ring[(index + 1) % ring.Length]) is { } crossing)
                    cuts.Add(crossing);
            }
        cuts.Sort();
        for (var index = 1; index < cuts.Count; index++)
            if (cuts[index] > cuts[index - 1])
                yield return (Lerp(from, to, cuts[index - 1]), Lerp(from, to, cuts[index]));
    }

    private bool OnOutline(PlanePoint point, double tolerance, out (PlanePoint From, PlanePoint To) along)
    {
        foreach (var edge in Edges)
            if (DistanceToSegment(point, edge.From, edge.To) <= tolerance)
            {
                along = edge;
                return true;
            }
        along = default;
        return false;
    }

    private bool Encloses(PlanePoint point)
    {
        var inside = false;
        foreach (var (from, to) in Edges)
            if ((from.Y > point.Y) != (to.Y > point.Y) && point.X < (to.X - from.X) * (point.Y - from.Y) / (to.Y - from.Y) + from.X)
                inside = !inside;
        return inside;
    }

    private static double? Crossing(PlanePoint from, PlanePoint to, PlanePoint start, PlanePoint end)
    {
        var denominator = (to.X - from.X) * (end.Y - start.Y) - (to.Y - from.Y) * (end.X - start.X);
        if (denominator == 0) return null;
        var alongThis = ((start.X - from.X) * (end.Y - start.Y) - (start.Y - from.Y) * (end.X - start.X)) / denominator;
        var alongOther = ((start.X - from.X) * (to.Y - from.Y) - (start.Y - from.Y) * (to.X - from.X)) / denominator;
        return alongThis is > 0 and < 1 && alongOther is >= 0 and <= 1 ? alongThis : null;
    }

    private static double Side(PlanePoint from, PlanePoint to, PlanePoint point, double tolerance)
    {
        var side = ((to.X - from.X) * (point.Y - from.Y) - (to.Y - from.Y) * (point.X - from.X)) / Distance(from, to);
        return Math.Abs(side) <= tolerance ? 0 : side;
    }

    private static double Nearest(IEnumerable<PlanePoint> points, IEnumerable<(PlanePoint From, PlanePoint To)> segments)
    {
        var listed = segments.ToList();
        return points.SelectMany(point => listed.Select(segment => DistanceToSegment(point, segment.From, segment.To))).DefaultIfEmpty(double.PositiveInfinity).Min();
    }

    private static double DistanceToSegment(PlanePoint point, PlanePoint from, PlanePoint to) =>
        Distance(point, Lerp(from, to, Math.Clamp(Along(point, from, to), 0, 1)));

    private static double Along(PlanePoint point, PlanePoint from, PlanePoint to)
    {
        var lengthSquared = (to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y);
        return lengthSquared == 0 ? 0 : ((point.X - from.X) * (to.X - from.X) + (point.Y - from.Y) * (to.Y - from.Y)) / lengthSquared;
    }

    private static double Dot(PlanePoint from, PlanePoint to, PlanePoint otherFrom, PlanePoint otherTo) =>
        (to.X - from.X) * (otherTo.X - otherFrom.X) + (to.Y - from.Y) * (otherTo.Y - otherFrom.Y);

    private static PlanePoint Lerp(PlanePoint from, PlanePoint to, double share) =>
        new(from.X + (to.X - from.X) * share, from.Y + (to.Y - from.Y) * share);

    private static PlanePoint Middle(PlanePoint from, PlanePoint to) => Lerp(from, to, 0.5);

    private static double Distance(PlanePoint from, PlanePoint to) => Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));

    private static double SignedArea(PlanePoint[] ring) =>
        ring.Select((corner, index) => (corner.X * ring[(index + 1) % ring.Length].Y - ring[(index + 1) % ring.Length].X * corner.Y) / 2).Sum();

    private static PlanePoint[] Turned(IReadOnlyList<PlanePoint> ring, bool anticlockwise)
    {
        var corners = ring.ToArray();
        if (SignedArea(corners) > 0 != anticlockwise)
            Array.Reverse(corners);
        return corners;
    }
}
