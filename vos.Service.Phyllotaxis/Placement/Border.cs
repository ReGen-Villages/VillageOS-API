namespace vos.Service.Phyllotaxis.Placement;

public sealed class Border(IReadOnlyList<Point> corners)
{
    public IReadOnlyList<Point> Corners { get; } = corners;

    public bool Holds(Point point)
    {
        var inside = false;
        for (int here = 0, previous = Corners.Count - 1; here < Corners.Count; previous = here++)
        {
            var a = Corners[here];
            var b = Corners[previous];
            if ((a.North > point.North) != (b.North > point.North)
                && point.East < (b.East - a.East) * (point.North - a.North) / (b.North - a.North) + a.East)
                inside = !inside;
        }

        return inside;
    }

    public bool HoldsCircle(Point centre, double radiusMetres)
    {
        if (!Holds(centre)) return false;

        for (int here = 0, previous = Corners.Count - 1; here < Corners.Count; previous = here++)
            if (DistanceToEdge(centre, Corners[previous], Corners[here]) < radiusMetres)
                return false;

        return true;
    }

    private static double DistanceToEdge(Point point, Point start, Point end)
    {
        var east = end.East - start.East;
        var north = end.North - start.North;
        var lengthSquared = east * east + north * north;
        var along = lengthSquared == 0
            ? 0
            : Math.Clamp(((point.East - start.East) * east + (point.North - start.North) * north) / lengthSquared, 0, 1);
        return point.DistanceTo(new Point(start.East + along * east, start.North + along * north));
    }
}
