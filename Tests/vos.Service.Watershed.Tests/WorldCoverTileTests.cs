using FluentAssertions;
using vos.Service.Watershed.LandCover;
using vos.Service.Watershed.Terrain;
using Xunit;

namespace vos.Service.Watershed.Tests;

public class WorldCoverTileTests
{
    private const double Pixel = BuiltTile.DegreesPerPixel;

    // The product of row and column keeps each block from compressing into the first read, so every block is asked for.
    private static byte CodeAt(int row, int column) => (byte)((row * 31 + column * 17 + row * column) % 251);

    private static readonly BuiltTile ThreeBlocksByTwo = new() { Width = 2 * 1024 + 100, Height = 1024 + 50, CodeAtRowAndColumn = CodeAt };

    private static async Task<List<LandCoverPixel>> PixelsAsync(
        BuiltTile built, int firstRow, int lastRow, int firstColumn, int lastColumn, RangesOver? ranges = null)
    {
        ranges ??= new RangesOver(built.Build().Bytes);
        var tile = await WorldCoverTile.OpenAsync(ranges.Read, CancellationToken.None);
        var (south, north) = (built.CentreOf(lastRow, 0).Latitude, built.CentreOf(firstRow, 0).Latitude);
        var (west, east) = (built.CentreOf(0, firstColumn).Longitude, built.CentreOf(0, lastColumn).Longitude);
        List<LandCoverPixel> pixels = [];
        await foreach (var pixel in tile.PixelsWithinAsync(south, west, north, east))
            pixels.Add(pixel);
        return pixels;
    }

    private static (int Row, int Column) RowAndColumnOf(BuiltTile built, Position centre) =>
        ((int)Math.Floor((built.North - centre.Latitude) / Pixel), (int)Math.Floor((centre.Longitude - built.West) / Pixel));

    private static IEnumerable<(int Row, int Column)> Window(int firstRow, int lastRow, int firstColumn, int lastColumn) =>
        from row in Enumerable.Range(firstRow, lastRow - firstRow + 1)
        from column in Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
        select (row, column);

    [Fact]
    public async Task Each_pixel_is_read_from_its_own_block_across_a_block_edge()
    {
        var pixels = await PixelsAsync(ThreeBlocksByTwo, 1020, 1027, 1020, 1027);

        pixels.Select(pixel => RowAndColumnOf(ThreeBlocksByTwo, pixel.Centre)).Should().BeEquivalentTo(Window(1020, 1027, 1020, 1027));
        pixels.Should().OnlyContain(pixel => pixel.Code == CodeAt(RowAndColumnOf(ThreeBlocksByTwo, pixel.Centre).Row, RowAndColumnOf(ThreeBlocksByTwo, pixel.Centre).Column));
    }

    [Fact]
    public async Task The_last_partial_block_is_read_up_to_the_rasters_edge_and_no_further()
    {
        var pixels = await PixelsAsync(ThreeBlocksByTwo, 1068, 1080, 2140, 2160);

        pixels.Select(pixel => RowAndColumnOf(ThreeBlocksByTwo, pixel.Centre)).Should().BeEquivalentTo(Window(1068, 1073, 2140, 2147));
        pixels.Should().OnlyContain(pixel => pixel.Code == CodeAt(RowAndColumnOf(ThreeBlocksByTwo, pixel.Centre).Row, RowAndColumnOf(ThreeBlocksByTwo, pixel.Centre).Column));
    }

    [Fact]
    public async Task A_pixels_centre_and_size_come_from_the_pixel_scale_and_the_tie_point()
    {
        var built = ThreeBlocksByTwo with { TiedPixel = (Column: 100, Row: 200) };

        var pixel = (await PixelsAsync(built, 5, 5, 7, 7)).Should().ContainSingle().Subject;

        pixel.Centre.Latitude.Should().BeApproximately(built.North - 5.5 * Pixel, 1e-12);
        pixel.Centre.Longitude.Should().BeApproximately(built.West + 7.5 * Pixel, 1e-12);
        pixel.Code.Should().Be(CodeAt(5, 7));
        pixel.DegreesAcross.Should().Be(Pixel);
        pixel.DegreesDown.Should().Be(Pixel);
    }

    [Fact]
    public async Task An_empty_block_reads_as_no_data_and_is_not_asked_for()
    {
        var built = ThreeBlocksByTwo with { WritesBlock = block => block != 0 };
        var ranges = new RangesOver(built.Build().Bytes);

        var pixels = await PixelsAsync(built, 10, 12, 10, 12, ranges);

        pixels.Should().HaveCount(9).And.OnlyContain(pixel => pixel.Code == 0);
        ranges.Asked.Should().Equal((0L, WorldCoverTile.FirstReadBytes));
    }

    [Fact]
    public async Task The_first_read_is_the_header_and_then_only_the_block_the_window_touches_is_asked_for()
    {
        var built = ThreeBlocksByTwo;
        var file = built.Build();
        var ranges = new RangesOver(file.Bytes);

        await PixelsAsync(built, 1030, 1040, 1030, 1040, ranges);

        ranges.Asked.Should().Equal((0L, WorldCoverTile.FirstReadBytes), file.Blocks[4]);
    }

    [Fact]
    public async Task Block_tables_past_the_first_read_are_asked_for_next_each_in_one_range()
    {
        var built = ThreeBlocksByTwo with { PaddingBeforeTables = 2 * WorldCoverTile.FirstReadBytes };
        var file = built.Build();
        var ranges = new RangesOver(file.Bytes);

        await PixelsAsync(built, 1030, 1040, 1030, 1040, ranges);

        ranges.Asked.Should().Equal((0L, WorldCoverTile.FirstReadBytes), file.BlockOffsets, file.BlockByteCounts, file.Blocks[4]);
    }

    [Fact]
    public async Task A_window_beside_the_raster_reads_no_pixel_and_asks_for_no_block()
    {
        var ranges = new RangesOver(ThreeBlocksByTwo.Build().Bytes);
        var tile = await WorldCoverTile.OpenAsync(ranges.Read, CancellationToken.None);
        List<LandCoverPixel> pixels = [];

        await foreach (var pixel in tile.PixelsWithinAsync(ThreeBlocksByTwo.North + 0.1, ThreeBlocksByTwo.West, ThreeBlocksByTwo.North + 0.2, ThreeBlocksByTwo.West + 0.1))
            pixels.Add(pixel);

        pixels.Should().BeEmpty();
        ranges.Asked.Should().HaveCount(1);
    }

    [Fact]
    public async Task The_tag_saying_no_predictor_is_read_as_ESA_writes_it()
    {
        var pixels = await PixelsAsync(ThreeBlocksByTwo with { Predictor = 1 }, 0, 0, 0, 0);

        pixels.Should().ContainSingle().Which.Code.Should().Be(CodeAt(0, 0));
    }

    public static TheoryData<string, string> OtherLayouts() => new()
    {
        { "big-endian", "big-endian" },
        { "BigTIFF", "BigTIFF" },
        { "compression 5", "compression 5" },
        { "16 bits", "16 bits" },
        { "3 samples", "3 samples" },
        { "stripped", "stripped" },
        { "tiles of 512", "512" },
        { "predictor 2", "predictor 2" },
        { "planar configuration 2", "planar configuration 2" },
        { "no pixel scale", "no pixel scale" },
    };

    [Theory]
    [MemberData(nameof(OtherLayouts))]
    public async Task A_file_in_any_layout_but_ESAs_is_refused_by_name(string layout, string named)
    {
        var built = layout switch
        {
            "big-endian" => ThreeBlocksByTwo with { ByteOrder = "MM" },
            "BigTIFF" => ThreeBlocksByTwo with { Version = 43 },
            "compression 5" => ThreeBlocksByTwo with { Compression = 5 },
            "16 bits" => ThreeBlocksByTwo with { BitsPerSample = 16 },
            "3 samples" => ThreeBlocksByTwo with { SamplesPerPixel = 3 },
            "stripped" => ThreeBlocksByTwo with { Stripped = true },
            "tiles of 512" => ThreeBlocksByTwo with { BlockSize = 512 },
            "predictor 2" => ThreeBlocksByTwo with { Predictor = 2 },
            "planar configuration 2" => ThreeBlocksByTwo with { PlanarConfiguration = 2 },
            _ => ThreeBlocksByTwo with { GeoReferenced = false },
        };

        var open = () => WorldCoverTile.OpenAsync(new RangesOver(built.Build().Bytes).Read, CancellationToken.None);

        await open.Should().ThrowAsync<UnreadableTileException>().WithMessage($"*{named}*");
    }

    [Fact]
    public async Task A_block_that_does_not_decompress_to_a_whole_block_is_refused_by_name()
    {
        var file = ThreeBlocksByTwo.Build();
        var (offset, length) = file.Blocks[0];
        var cut = file.Bytes.Take((int)offset + length / 2).Concat(new byte[length - length / 2]).ToArray();
        var tile = await WorldCoverTile.OpenAsync(new RangesOver(cut).Read, CancellationToken.None);

        var read = async () =>
        {
            await foreach (var _ in tile.PixelsWithinAsync(ThreeBlocksByTwo.CentreOf(1, 0).Latitude, ThreeBlocksByTwo.West, ThreeBlocksByTwo.North, ThreeBlocksByTwo.CentreOf(0, 1).Longitude))
            {
            }
        };

        await read.Should().ThrowAsync<UnreadableTileException>().WithMessage("*block 0*");
    }
}
