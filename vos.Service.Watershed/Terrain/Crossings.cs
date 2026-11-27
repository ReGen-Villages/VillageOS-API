namespace vos.Service.Watershed.Terrain;

public static class Crossings
{
    public static bool Encloses(IReadOnlyList<(double Across, double Up)> outline, double across, double up)
    {
        var enclosed = false;
        for (int corner = 0, previous = outline.Count - 1; corner < outline.Count; previous = corner++)
        {
            var (here, there) = (outline[corner], outline[previous]);
            if ((here.Up > up) != (there.Up > up)
                && across < (there.Across - here.Across) * (up - here.Up) / (there.Up - here.Up) + here.Across)
                enclosed = !enclosed;
        }
        return enclosed;
    }
}
