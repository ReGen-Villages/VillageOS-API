using FluentAssertions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

public class WindRoseTests
{
    private static readonly string[] SectorNames =
    [
        "north", "north-northeast", "northeast", "east-northeast", "east", "east-southeast", "southeast", "south-southeast",
        "south", "south-southwest", "southwest", "west-southwest", "west", "west-northwest", "northwest", "north-northwest",
    ];

    private static readonly (string Name, double? AtLeast, double? Below)[] BandBounds =
    [
        ("under-2", null, 2.0), ("2-to-3", 2.0, 3.0), ("3-to-5", 3.0, 5.0), ("5-to-9", 5.0, 9.0), ("over-9", 9.0, null),
    ];

    private sealed class Vocabulary
    {
        public Dictionary<string, Guid> Sectors { get; } = [];
        public Dictionary<string, Guid> Bands { get; } = [];
        public Dictionary<(string Sector, string Band), Guid> Cells { get; } = [];
        public WindRose Rose { get; }

        public Vocabulary(int sectorCount)
        {
            var width = 360.0 / sectorCount;
            var sectors = Enumerable.Range(0, sectorCount).Select(at =>
            {
                var name = sectorCount == SectorNames.Length ? SectorNames[at] : $"sector {at}";
                Sectors[name] = Guid.NewGuid();
                return new WindSector(Sectors[name], at * width);
            }).ToList();
            var bands = BandBounds.Select(band =>
            {
                Bands[band.Name] = Guid.NewGuid();
                return new WindSpeedBand(Bands[band.Name], band.AtLeast, band.Below);
            }).ToList();
            foreach (var sector in Sectors)
                foreach (var band in Bands)
                    Cells[(sector.Key, band.Key)] = Guid.NewGuid();
            Rose = new WindRose(sectors, bands, Cells.ToDictionary(cell => (Sectors[cell.Key.Sector], Bands[cell.Key.Band]), cell => cell.Value));
        }
    }

    private static readonly Vocabulary Sixteen = new(16);

    [Theory]
    [InlineData(100.0, 3.4, "east", "3-to-5")]
    [InlineData(0.0, 1.0, "north", "under-2")]
    [InlineData(348.75, 1.0, "north", "under-2")]
    [InlineData(348.74, 1.0, "north-northwest", "under-2")]
    [InlineData(11.25, 1.0, "north-northeast", "under-2")]
    [InlineData(11.24, 1.0, "north", "under-2")]
    [InlineData(359.99, 2.0, "north", "2-to-3")]
    [InlineData(180.0, 9.0, "south", "over-9")]
    [InlineData(270.0, 8.99, "west", "5-to-9")]
    public void An_hour_is_in_the_cell_whose_sector_holds_its_direction_and_whose_class_holds_its_speed(
        double direction, double speed, string sector, string band)
    {
        Sixteen.Rose.CellOf(direction, speed).Should().Be(Sixteen.Cells[(sector, band)]);
    }

    [Fact]
    public void A_vocabulary_of_eight_sectors_divides_the_circle_into_eighths()
    {
        var eight = new Vocabulary(8);

        eight.Rose.CellOf(22.4, 1.0).Should().Be(eight.Cells[("sector 0", "under-2")]);
        eight.Rose.CellOf(22.5, 1.0).Should().Be(eight.Cells[("sector 1", "under-2")]);
    }

    // The rose's shares are taken over the hours that have a cell, so a calm hour counts in none of them.
    [Fact]
    public void A_calm_hour_has_no_cell()
    {
        Sixteen.Rose.CellOf(90.0, 0.0).Should().BeNull();
    }

    [Theory]
    [InlineData(null, 3.0)]
    [InlineData(90.0, null)]
    public void An_hour_missing_its_direction_or_its_speed_has_no_cell(double? direction, double? speed)
    {
        Sixteen.Rose.CellOf(direction, speed).Should().BeNull();
    }

    [Fact]
    public void A_model_without_the_wind_rose_vocabulary_gives_no_cell()
    {
        new WindRose([], [], new Dictionary<(Guid, Guid), Guid>()).CellOf(90.0, 3.0).Should().BeNull();
    }
}
