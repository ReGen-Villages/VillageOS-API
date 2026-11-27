using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.LandCover;

public delegate Task<byte[]> ReadRange(long offset, int length, CancellationToken cancellationToken);

public readonly record struct LandCoverPixel(Position Centre, double DegreesAcross, double DegreesDown, byte Code);

public sealed class UnreadableTileException(string message) : Exception(message);

// Reads the one layout ESA writes WorldCover in, a classic little-endian TIFF of deflated 1,024-pixel blocks of one
// byte each, and refuses any other by name rather than misread it.
public sealed class WorldCoverTile
{
    public const int FirstReadBytes = 16384;
    public const int BlockSize = 1024;

    private const ushort Deflate = 8;
    private const int EntryBytes = 12;

    private readonly ReadRange _read;
    private readonly byte[] _first;
    private readonly int _width;
    private readonly int _height;
    private readonly int _blocksAcross;
    private readonly long[] _blockOffsets;
    private readonly long[] _blockByteCounts;
    private readonly double _west;
    private readonly double _north;
    private readonly double _degreesAcross;
    private readonly double _degreesDown;

    private WorldCoverTile(ReadRange read, byte[] first, int width, int height, long[] blockOffsets, long[] blockByteCounts,
        double west, double north, double degreesAcross, double degreesDown)
    {
        (_read, _first, _width, _height) = (read, first, width, height);
        _blocksAcross = (width + BlockSize - 1) / BlockSize;
        (_blockOffsets, _blockByteCounts) = (blockOffsets, blockByteCounts);
        (_west, _north, _degreesAcross, _degreesDown) = (west, north, degreesAcross, degreesDown);
    }

    public static async Task<WorldCoverTile> OpenAsync(ReadRange read, CancellationToken cancellationToken)
    {
        var first = await read(0, FirstReadBytes, cancellationToken);
        async Task<byte[]> At(long offset, int length) => await ReadAtAsync(read, first, offset, length, cancellationToken);

        var header = await At(0, 8);
        if (header[0] == 'M' && header[1] == 'M') throw new UnreadableTileException("the file is big-endian, not ESA's little-endian");
        if (header[0] != 'I' || header[1] != 'I') throw new UnreadableTileException("the file is not a TIFF");
        var version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
        if (version == 43) throw new UnreadableTileException("the file is a BigTIFF, not ESA's classic TIFF");
        if (version != 42) throw new UnreadableTileException($"the file is TIFF version {version}, not 42");

        var directory = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(await At(directory, 2));
        var entries = await At(directory + 2, EntryBytes * count);
        var tags = Enumerable.Range(0, count).Select(index => entries.AsMemory(EntryBytes * index, EntryBytes))
            .ToDictionary(entry => BinaryPrimitives.ReadUInt16LittleEndian(entry.Span), entry => entry);

        async Task<double[]> Values(ushort tag)
        {
            if (!tags.TryGetValue(tag, out var entry)) return [];
            var type = BinaryPrimitives.ReadUInt16LittleEndian(entry.Span[2..]);
            var number = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry.Span[4..]);
            var size = type switch
            {
                3 => 2,
                4 => 4,
                12 => 8,
                _ => throw new UnreadableTileException($"tag {tag} is of TIFF type {type}, which ESA does not write"),
            };
            var bytes = size * number <= 4
                ? entry[8..12].ToArray()
                : await At(BinaryPrimitives.ReadUInt32LittleEndian(entry.Span[8..]), size * number);
            return [.. Enumerable.Range(0, number).Select(index => type switch
            {
                3 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * index)),
                4 => (double)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 * index)),
                _ => BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(8 * index)),
            })];
        }
        async Task<double> Number(ushort tag, double absent) => await Values(tag) is [var value, ..] ? value : absent;

        Refuse(await Number(259, 1), Deflate, compression => $"compression {compression}, not ESA's {Deflate}");
        Refuse(await Number(258, 1), 8, bits => $"{bits} bits a sample, not ESA's 8");
        Refuse(await Number(277, 1), 1, samples => $"{samples} samples a pixel, not ESA's 1");
        Refuse(await Number(284, 1), 1, planar => $"planar configuration {planar}, not ESA's 1");
        Refuse(await Number(317, 1), 1, predictor => $"predictor {predictor}, not ESA's none");
        if (!tags.ContainsKey(322)) throw new UnreadableTileException("the file is stripped, not tiled in blocks as ESA writes it");
        var (blockWidth, blockLength) = (await Number(322, 0), await Number(323, 0));
        if (blockWidth != BlockSize || blockLength != BlockSize)
            throw new UnreadableTileException($"the blocks are {blockWidth} × {blockLength} pixels, not ESA's {BlockSize} × {BlockSize}");
        if (await Values(33550) is not [var degreesAcross, var degreesDown, ..] || await Values(33922) is not [var tiedColumn, var tiedRow, _, var tiedLongitude, var tiedLatitude, ..])
            throw new UnreadableTileException("the file holds no pixel scale or no tie point, so its pixels have no place");

        var (width, height) = ((int)await Number(256, 0), (int)await Number(257, 0));
        var blocks = (width + BlockSize - 1) / BlockSize * ((height + BlockSize - 1) / BlockSize);
        var offsets = await Values(324);
        var byteCounts = await Values(325);
        if (offsets.Length != blocks || byteCounts.Length != blocks)
            throw new UnreadableTileException($"the block tables hold {offsets.Length} and {byteCounts.Length} blocks, not the {blocks} of a {width} × {height} raster");

        return new WorldCoverTile(read, first, width, height, [.. offsets.Select(offset => (long)offset)], [.. byteCounts.Select(byteCount => (long)byteCount)],
            tiedLongitude - tiedColumn * degreesAcross, tiedLatitude + tiedRow * degreesDown, degreesAcross, degreesDown);
    }

    public async IAsyncEnumerable<LandCoverPixel> PixelsWithinAsync(
        double south, double west, double north, double east, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var firstColumn = Math.Max(0, (int)Math.Floor((west - _west) / _degreesAcross));
        var lastColumn = Math.Min(_width - 1, (int)Math.Floor((east - _west) / _degreesAcross));
        var firstRow = Math.Max(0, (int)Math.Floor((_north - north) / _degreesDown));
        var lastRow = Math.Min(_height - 1, (int)Math.Floor((_north - south) / _degreesDown));
        if (firstColumn > lastColumn || firstRow > lastRow) yield break;

        for (var blockRow = firstRow / BlockSize; blockRow <= lastRow / BlockSize; blockRow++)
            for (var blockColumn = firstColumn / BlockSize; blockColumn <= lastColumn / BlockSize; blockColumn++)
            {
                var block = await BlockAsync(blockRow * _blocksAcross + blockColumn, cancellationToken);
                for (var row = Math.Max(firstRow, blockRow * BlockSize); row <= Math.Min(lastRow, blockRow * BlockSize + BlockSize - 1); row++)
                    for (var column = Math.Max(firstColumn, blockColumn * BlockSize); column <= Math.Min(lastColumn, blockColumn * BlockSize + BlockSize - 1); column++)
                        yield return new LandCoverPixel(
                            new Position(_north - (row + 0.5) * _degreesDown, _west + (column + 0.5) * _degreesAcross), _degreesAcross, _degreesDown,
                            block is null ? (byte)0 : block[(row - blockRow * BlockSize) * BlockSize + column - blockColumn * BlockSize]);
            }
    }

    private async Task<byte[]?> BlockAsync(int block, CancellationToken cancellationToken)
    {
        if (_blockByteCounts[block] == 0) return null;
        var compressed = await ReadAtAsync(_read, _first, _blockOffsets[block], (int)_blockByteCounts[block], cancellationToken);
        var pixels = new byte[BlockSize * BlockSize];
        var filled = 0;
        try
        {
            using var inflate = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            for (int read; filled < pixels.Length && (read = await inflate.ReadAsync(pixels.AsMemory(filled), cancellationToken)) > 0;)
                filled += read;
        }
        catch (InvalidDataException broken)
        {
            throw new UnreadableTileException($"block {block} cannot be decompressed: {broken.Message}");
        }
        if (filled < pixels.Length)
            throw new UnreadableTileException($"block {block} holds {filled} pixels, not the {pixels.Length} of a whole block");
        return pixels;
    }

    private static async Task<byte[]> ReadAtAsync(ReadRange read, byte[] first, long offset, int length, CancellationToken cancellationToken)
    {
        if (offset + length <= first.Length) return first[(int)offset..(int)(offset + length)];
        var bytes = first.Length < FirstReadBytes ? Array.Empty<byte>() : await read(offset, length, cancellationToken);
        if (bytes.Length < length)
            throw new UnreadableTileException($"the file ends before bytes {offset} to {offset + length - 1}");
        return bytes;
    }

    private static void Refuse(double found, double expected, Func<double, string> differs)
    {
        if (found != expected) throw new UnreadableTileException(differs(found));
    }
}
