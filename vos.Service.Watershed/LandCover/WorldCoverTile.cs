using System.Runtime.CompilerServices;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.LandCover;

public delegate Task<byte[]> ReadRange(long offset, int length, CancellationToken cancellationToken);

public readonly record struct LandCoverPixel(Position Centre, double DegreesAcross, double DegreesDown, byte Code);

public sealed class UnreadableTileException(string message) : Exception(message);

public sealed class WorldCoverTile
{
    public const int FirstReadBytes = 16384;
    public const int BlockSize = 1024;

    private WorldCoverTile()
    {
    }

    public static Task<WorldCoverTile> OpenAsync(ReadRange read, CancellationToken cancellationToken) => Task.FromResult(new WorldCoverTile());

    public async IAsyncEnumerable<LandCoverPixel> PixelsWithinAsync(
        double south, double west, double north, double east, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
