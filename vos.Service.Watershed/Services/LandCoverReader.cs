using vos.Service.Watershed.LandCover;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed class LandCoverReader(IHttpClientFactory httpClientFactory)
{
    public const string TilePlaceholder = "{tile}";

    public Task<IReadOnlyList<LandCoverShare>> SharesAsync(
        string tileAddress, IReadOnlyList<Position> border, IReadOnlyList<LandCoverClassCode> classes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LandCoverShare>>([]);
}
