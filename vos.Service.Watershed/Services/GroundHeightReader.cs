using vos.Service.Shared;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

// Asks the platform's fetcher, which holds the provider's address, so no address or limit is written here.
public sealed class GroundHeightReader : MyceliumClientBase
{
    public const int MostPointsInACall = 100;
    public static readonly TimeSpan GapBetweenCalls = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock;
    private readonly string _fetcherSubdomain;

    public GroundHeightReader(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, TimeProvider clock,
        LaunchToken? launchToken = null, string? apiKey = null, string fetcherSubdomain = OutsideCalls.FetcherSubdomain)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
    {
        _clock = clock;
        _fetcherSubdomain = fetcherSubdomain;
    }

    public Task<IReadOnlyList<double>> HeightsAtAsync(string endpointName, IReadOnlyList<Position> points, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
