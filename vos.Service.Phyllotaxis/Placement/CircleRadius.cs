namespace vos.Service.Phyllotaxis.Placement;

public static class CircleRadius
{
    public static double OfArea(double squareMetres) => Math.Sqrt(squareMetres / Math.PI);
}
