using System.Text.Json;
using FluentAssertions;
using vos.Service.Watershed.LandCover;
using Xunit;

namespace vos.Service.Watershed.Tests;

// The pinned figures come from a separate Python decode of ESA's recorded bytes, so the reader is checked against ESA's
// real file and not only against the test project's own TIFF writer.
public class ReferenceParcelLandCoverTests
{
    private static readonly int[] EsaCodes = [10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 100];

    public sealed record RecordedRange(string File, long Offset, int Length);

    public sealed record Recording(string Source, string Credit, RecordedRange Header, RecordedRange Block);

    private static string RecordedPath(string file) => Path.Combine(AppContext.BaseDirectory, "Recorded", file);

    private static readonly Recording Recorded = JsonSerializer.Deserialize<Recording>(
        File.ReadAllText(RecordedPath("worldcover-N24E054.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static Task<byte[]> ReadRecorded(long offset, int length, CancellationToken cancellationToken)
    {
        if (offset + length <= Recorded.Header.Length)
            return Task.FromResult(File.ReadAllBytes(RecordedPath(Recorded.Header.File))[(int)offset..(int)(offset + length)]);
        if (offset == Recorded.Block.Offset && length == Recorded.Block.Length)
            return Task.FromResult(File.ReadAllBytes(RecordedPath(Recorded.Block.File)));
        throw new InvalidOperationException($"bytes {offset} to {offset + length - 1} were not recorded");
    }

    [Fact]
    public void The_recording_carries_ESAs_credit_and_the_address_it_was_read_from()
    {
        Recorded.Credit.Should().Contain("ESA WorldCover project 2021").And.Contain("CC BY 4.0");
        Recorded.Source.Should().EndWith("ESA_WorldCover_10m_2021_v200_N24E054_Map.tif");
    }

    [Fact]
    public async Task The_reference_parcel_has_the_shares_a_separate_decode_of_ESAs_bytes_gives()
    {
        var border = HeightGridLayoutTests.ReferenceBorder;
        var classes = EsaCodes.Select(code => new LandCoverClassCode(Guid.NewGuid(), code)).ToArray();
        var shares = new LandCoverShares(border, classes);

        var tile = await WorldCoverTile.OpenAsync(ReadRecorded, CancellationToken.None);
        await foreach (var pixel in tile.PixelsWithinAsync(
            border.Min(corner => corner.Latitude), border.Min(corner => corner.Longitude),
            border.Max(corner => corner.Latitude), border.Max(corner => corner.Longitude)))
            shares.Count(pixel);

        var byCode = shares.Shares.ToDictionary(share => classes.Single(landCover => landCover.Term == share.Term).Code);
        byCode[60].PixelCount.Should().Be(8031);
        byCode[60].AreaHectares.Should().BeApproximately(62.138941535777214, 1e-9);
        byCode[60].SharePercent.Should().BeApproximately(99.96265980377397, 1e-9);
        byCode[20].PixelCount.Should().Be(3);
        byCode[20].AreaHectares.Should().BeApproximately(0.023211469910643108, 1e-9);
        byCode[20].SharePercent.Should().BeApproximately(0.037340196226020764, 1e-9);
        byCode.Where(each => each.Key is not (20 or 60)).Should().OnlyContain(each => each.Value.PixelCount == 0 && each.Value.SharePercent == 0);
    }
}
