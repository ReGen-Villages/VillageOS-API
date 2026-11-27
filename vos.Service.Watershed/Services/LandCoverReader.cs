using System.Net;
using System.Net.Http.Headers;
using vos.Service.Shared.DagNode;
using vos.Service.Watershed.LandCover;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed class LandCoverReader(IHttpClientFactory httpClientFactory)
{
    public const string TilePlaceholder = "{tile}";

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<LandCoverShare>> SharesAsync(
        string tileAddress, IReadOnlyList<Position> border, IReadOnlyList<LandCoverClassCode> classes, CancellationToken cancellationToken)
    {
        var (south, north) = (border.Min(corner => corner.Latitude), border.Max(corner => corner.Latitude));
        var (west, east) = (border.Min(corner => corner.Longitude), border.Max(corner => corner.Longitude));
        var shares = new LandCoverShares(border, classes);
        var client = httpClientFactory.CreateClient(nameof(LandCoverReader));
        client.Timeout = CallTimeout;

        foreach (var name in WorldCoverTiles.Touching(south, west, north, east))
        {
            var address = tileAddress.Replace(TilePlaceholder, name, StringComparison.Ordinal);
            try
            {
                var tile = await WorldCoverTile.OpenAsync((offset, length, token) => RangeAsync(client, address, offset, length, token), cancellationToken);
                await foreach (var pixel in tile.PixelsWithinAsync(south, west, north, east, cancellationToken))
                    shares.Count(pixel);
            }
            catch (NoTileException)
            {
            }
            catch (UnreadableTileException unreadable)
            {
                throw new NodeFailedException($"The land cover tile {name} cannot be read: {unreadable.Message}.");
            }
        }
        return shares.Shares;
    }

    private static async Task<byte[]> RangeAsync(HttpClient client, string address, long offset, int length, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception unreached) when (unreached is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new FailureCanBeTriedAgainException($"The land cover source could not be reached: {unreached.Message}");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) throw new NoTileException();
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                throw new FailureCanBeTriedAgainException(
                    $"The land cover source answered {(int)response.StatusCode}, so the land cover is worked out later.", response.Headers.RetryAfter?.Delta);
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new NodeFailedException($"The land cover source answered {(int)response.StatusCode} to a byte range, not 206 with the bytes asked for.");
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
    }

    // ESA holds no tile where there is no land, such as open sea, and answers 404 for it.
    private sealed class NoTileException : Exception
    {
    }
}
