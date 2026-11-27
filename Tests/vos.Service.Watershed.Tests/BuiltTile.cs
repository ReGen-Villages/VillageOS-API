using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using vos.Service.Watershed.LandCover;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Tests;

public sealed record BuiltFile(byte[] Bytes, (long Offset, int Length)[] Blocks, (long Offset, int Length) BlockOffsets, (long Offset, int Length) BlockByteCounts);

public sealed record BuiltTile
{
    public const double DegreesPerPixel = 1 / 12000.0;

    private const ushort Short = 3;
    private const ushort Long = 4;
    private const ushort Double = 12;
    private const ushort Palette = 3;
    private const ushort TileOffsetsTag = 324;
    private const ushort TileByteCountsTag = 325;

    public int Width { get; init; } = 36000;
    public int Height { get; init; } = 36000;
    public int BlockSize { get; init; } = WorldCoverTile.BlockSize;
    public double West { get; init; } = 54;
    public double North { get; init; } = 27;
    public (int Column, int Row) TiedPixel { get; init; }
    public Func<int, int, byte> CodeAtRowAndColumn { get; init; } = (_, _) => 0;
    public Func<int, bool> WritesBlock { get; init; } = _ => true;
    public string ByteOrder { get; init; } = "II";
    public ushort Version { get; init; } = 42;
    public ushort Compression { get; init; } = 8;
    public ushort BitsPerSample { get; init; } = 8;
    public ushort SamplesPerPixel { get; init; } = 1;
    public ushort? Predictor { get; init; }
    public ushort PlanarConfiguration { get; init; } = 1;
    public bool Stripped { get; init; }
    public bool GeoReferenced { get; init; } = true;
    public int PaddingBeforeTables { get; init; }
    public int? PixelsInEachBlock { get; init; }

    public int BlocksAcross => (Width + BlockSize - 1) / BlockSize;
    public int BlocksDown => (Height + BlockSize - 1) / BlockSize;

    public Position CentreOf(int row, int column) => new(North - (row + 0.5) * DegreesPerPixel, West + (column + 0.5) * DegreesPerPixel);

    public static BuiltTile Named(string name)
    {
        var south = int.Parse(name[1..3]) * (name[0] == 'S' ? -1 : 1);
        var west = int.Parse(name[4..7]) * (name[3] == 'W' ? -1 : 1);
        return new BuiltTile { West = west, North = south + WorldCoverTiles.DegreesASide };
    }

    public BuiltTile Over(IReadOnlyList<Position> border, Func<Position, byte> codeAt)
    {
        var firstRow = (int)Math.Floor((North - border.Max(corner => corner.Latitude)) / DegreesPerPixel);
        var lastRow = (int)Math.Floor((North - border.Min(corner => corner.Latitude)) / DegreesPerPixel);
        var firstColumn = (int)Math.Floor((border.Min(corner => corner.Longitude) - West) / DegreesPerPixel);
        var lastColumn = (int)Math.Floor((border.Max(corner => corner.Longitude) - West) / DegreesPerPixel);
        return this with
        {
            CodeAtRowAndColumn = (row, column) => codeAt(CentreOf(row, column)),
            WritesBlock = block =>
                block / BlocksAcross * BlockSize <= lastRow && (block / BlocksAcross + 1) * BlockSize > firstRow
                && block % BlocksAcross * BlockSize <= lastColumn && (block % BlocksAcross + 1) * BlockSize > firstColumn,
        };
    }

    public BuiltFile Build()
    {
        var blockCount = BlocksAcross * BlocksDown;
        var blocks = Enumerable.Range(0, blockCount).Select(block => WritesBlock(block) ? Compressed(block) : Array.Empty<byte>()).ToArray();

        var entries = new List<Entry>
        {
            new(256, Long, Longs(Width)), new(257, Long, Longs(Height)), new(258, Short, Shorts(BitsPerSample)),
            new(259, Short, Shorts(Compression)), new(262, Short, Shorts(Palette)), new(277, Short, Shorts(SamplesPerPixel)),
        };
        if (Stripped) entries.Add(new(278, Long, Longs(BlockSize)));
        entries.Add(new(284, Short, Shorts(PlanarConfiguration)));
        if (Predictor is { } predictor) entries.Add(new(317, Short, Shorts(predictor)));
        if (!Stripped)
        {
            entries.Add(new(322, Short, Shorts((ushort)BlockSize)));
            entries.Add(new(323, Short, Shorts((ushort)BlockSize)));
            entries.Add(new(TileOffsetsTag, Long, new byte[4 * blockCount]));
            entries.Add(new(TileByteCountsTag, Long, new byte[4 * blockCount]));
        }
        if (GeoReferenced)
        {
            entries.Add(new(33550, Double, Doubles(DegreesPerPixel, DegreesPerPixel, 0)));
            entries.Add(new(33922, Double, Doubles(
                TiedPixel.Column, TiedPixel.Row, 0, West + TiedPixel.Column * DegreesPerPixel, North - TiedPixel.Row * DegreesPerPixel, 0)));
        }

        const int directoryStart = 8;
        long position = directoryStart + 2 + 12 * entries.Count + 4;
        var isTable = (Entry entry) => entry.Tag is TileOffsetsTag or TileByteCountsTag;
        foreach (var entry in entries.Where(entry => entry.Value.Length > 4 && !isTable(entry)))
            position = Place(entry, position);
        position += PaddingBeforeTables;
        foreach (var entry in entries.Where(entry => entry.Value.Length > 4 && isTable(entry)))
            position = Place(entry, position);

        var blockRanges = new (long Offset, int Length)[blockCount];
        for (var block = 0; block < blockCount; block++)
        {
            blockRanges[block] = blocks[block].Length == 0 ? (0, 0) : (position, blocks[block].Length);
            position += blocks[block].Length;
        }
        if (!Stripped)
        {
            var offsets = entries.Single(entry => entry.Tag == TileOffsetsTag);
            var counts = entries.Single(entry => entry.Tag == TileByteCountsTag);
            for (var block = 0; block < blockCount; block++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(offsets.Value.AsSpan(4 * block), (uint)blockRanges[block].Offset);
                BinaryPrimitives.WriteUInt32LittleEndian(counts.Value.AsSpan(4 * block), (uint)blockRanges[block].Length);
            }
        }

        var file = new byte[position];
        Encoding.ASCII.GetBytes(ByteOrder).CopyTo(file, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), Version);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), directoryStart);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(directoryStart), (ushort)entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var at = directoryStart + 2 + 12 * index;
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), entry.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 2), entry.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), (uint)(entry.Value.Length / SizeOf(entry.Type)));
            if (entry.Value.Length <= 4)
                entry.Value.CopyTo(file, at + 8);
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 8), (uint)entry.Offset);
                entry.Value.CopyTo(file, entry.Offset);
            }
        }
        for (var block = 0; block < blockCount; block++)
            blocks[block].CopyTo(file, blockRanges[block].Offset);

        (long, int) RangeOf(ushort tag) => entries.SingleOrDefault(entry => entry.Tag == tag) is { } table ? (table.Offset, table.Value.Length) : (0, 0);
        return new BuiltFile(file, blockRanges, RangeOf(TileOffsetsTag), RangeOf(TileByteCountsTag));
    }

    private static long Place(Entry entry, long position)
    {
        entry.Offset = position;
        return position + entry.Value.Length;
    }

    private byte[] Compressed(int block)
    {
        var (firstRow, firstColumn) = (block / BlocksAcross * BlockSize, block % BlocksAcross * BlockSize);
        var pixels = new byte[BlockSize * BlockSize];
        for (var row = 0; row < BlockSize && firstRow + row < Height; row++)
            for (var column = 0; column < BlockSize && firstColumn + column < Width; column++)
                pixels[row * BlockSize + column] = CodeAtRowAndColumn(firstRow + row, firstColumn + column);
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest))
            deflate.Write(pixels.AsSpan(0, PixelsInEachBlock ?? pixels.Length));
        return compressed.ToArray();
    }

    private static int SizeOf(ushort type) => type switch { Short => 2, Long => 4, _ => 8 };

    private static byte[] Shorts(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Longs(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
        return bytes;
    }

    private static byte[] Doubles(params double[] values)
    {
        var bytes = new byte[8 * values.Length];
        for (var index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(8 * index), values[index]);
        return bytes;
    }

    private sealed class Entry(ushort tag, ushort type, byte[] value)
    {
        public ushort Tag { get; } = tag;
        public ushort Type { get; } = type;
        public byte[] Value { get; } = value;
        public long Offset { get; set; }
    }
}

public sealed class RangesOver(byte[] file)
{
    public List<(long Offset, int Length)> Asked { get; } = [];

    public Task<byte[]> Read(long offset, int length, CancellationToken cancellationToken)
    {
        Asked.Add((offset, length));
        var start = (int)Math.Min(offset, file.Length);
        var end = (int)Math.Min(offset + length, file.Length);
        return Task.FromResult(file[start..end]);
    }
}
