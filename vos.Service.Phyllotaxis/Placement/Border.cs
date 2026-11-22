namespace vos.Service.Phyllotaxis.Placement;

public sealed class Border(IReadOnlyList<Point> corners)
{
    public IReadOnlyList<Point> Corners { get; } = corners;

    public bool Holds(Point point)
    {
        throw new NotImplementedException();
    }

    public bool HoldsCircle(Point centre, double radiusMetres)
    {
        throw new NotImplementedException();
    }
}
