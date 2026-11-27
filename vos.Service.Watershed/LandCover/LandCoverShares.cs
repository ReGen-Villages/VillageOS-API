using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.LandCover;

public sealed record LandCoverClassCode(Guid Term, int Code);

public sealed record LandCoverShare(Guid Term, int PixelCount, double AreaHectares, double SharePercent);

public sealed class LandCoverShares(IReadOnlyList<Position> border, IReadOnlyList<LandCoverClassCode> classes)
{
    public void Count(LandCoverPixel pixel)
    {
    }

    public IReadOnlyList<LandCoverShare> Shares => [];
}
