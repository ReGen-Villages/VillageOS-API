using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.LandCover;

public sealed record LandCoverClassCode(Guid Term, int Code);

public sealed record LandCoverShare(Guid Term, int PixelCount, double AreaHectares, double SharePercent);

public sealed class LandCoverShares
{
    private const double SquareMetresAHectare = 10000;

    private readonly (double Across, double Up)[] _outline;
    private readonly IReadOnlyList<LandCoverClassCode> _classes;
    private readonly Dictionary<int, int> _classAt;
    private readonly int[] _pixels;
    private readonly double[] _hectares;

    public LandCoverShares(IReadOnlyList<Position> border, IReadOnlyList<LandCoverClassCode> classes)
    {
        _outline = [.. border.Select(corner => (corner.Longitude, corner.Latitude))];
        _classes = classes;
        _classAt = classes.Select((landCover, index) => (landCover.Code, index)).DistinctBy(each => each.Code).ToDictionary(each => each.Code, each => each.index);
        _pixels = new int[classes.Count];
        _hectares = new double[classes.Count];
    }

    public void Count(LandCoverPixel pixel)
    {
        if (!_classAt.TryGetValue(pixel.Code, out var index) || !Crossings.Encloses(_outline, pixel.Centre.Longitude, pixel.Centre.Latitude))
            return;
        _pixels[index]++;
        _hectares[index] += pixel.DegreesDown * EarthDistances.MetresPerDegreeOfLatitude
                            * pixel.DegreesAcross * EarthDistances.MetresPerDegreeOfLongitudeAt(pixel.Centre.Latitude) / SquareMetresAHectare;
    }

    public IReadOnlyList<LandCoverShare> Shares
    {
        get
        {
            var total = _hectares.Sum();
            return total <= 0
                ? []
                : [.. _classes.Select((landCover, index) => new LandCoverShare(landCover.Term, _pixels[index], _hectares[index], 100 * _hectares[index] / total))];
        }
    }
}
