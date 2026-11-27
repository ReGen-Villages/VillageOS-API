using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Stomata.Services;
using Xunit;

namespace vos.Service.Stomata.Tests;

// A check that Stomata works the recorded hours out by the methods it names, against a separate implementation of them;
// each figure's acceptance stays its hand-worked test.
public class ReferenceImplementationTests
{
    private const double ReferenceRounding = 0.05;

    private static readonly string[] SpeedClasses = ["under-2", "2-to-3", "3-to-5", "5-to-9", "over-9"];
    private static readonly (double? AtLeast, double? Below)[] SpeedClassBounds = [(null, 2.0), (2.0, 3.0), (3.0, 5.0), (5.0, 9.0), (9.0, null)];

    private static readonly JsonElement Notice = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", "climate-point-hourly.json"))).RootElement;

    private static readonly IReadOnlyList<RecordedHour> Hours = ReadHours();

    private const string CoolingEffect = "above 0.2 m/s Stomata takes the vote at the cooling effect, ASHRAE 55-2017's elevated air speed "
                                         + "method, where the reference takes Fanger's vote at the moving air's own speed";

    private const string OwnHumidity = "Stomata works each hour's zone at that hour's own humidity, where the reference works zones "
                                       + "for each 10 % step of relative humidity";

    private static readonly Dictionary<string, string> NamedWindRoseDifferences = new()
    {
        ["270° under-2"] = "an hour with no wind has no direction, so Stomata leaves it out of the rose, where the reference counts the "
                           + "direction the provider writes for it",
    };

    private static readonly Dictionary<string, string> NamedDesignStrategyDifferences = new()
    {
        [DesignStrategy.ComfortableAsItIs] = OwnHumidity + ", and Stomata takes saturation pressure from ASHRAE Fundamentals 2017 "
                                             + "where the reference takes the Magnus formula",
        [DesignStrategy.WithAirMovement] = CoolingEffect,
        [DesignStrategy.DirectEvaporativeCooling] = CoolingEffect,
        [DesignStrategy.TwoStageEvaporativeCooling] = CoolingEffect,
        [DesignStrategy.DehumidificationNeeded] = CoolingEffect,
        [DesignStrategy.HeatingNeeded] = OwnHumidity,
        [DesignStrategy.CoolingSystemNeeded] = CoolingEffect,
    };

    private sealed record RecordedHour(DateTime At, int LocalYear, double AirCelsius, double RelativeHumidityPercent, double WindMetresPerSecond, double WindDegrees);

    [Fact]
    public void The_recorded_hours_are_the_ten_years_the_notice_names()
    {
        Hours.Should().HaveCount(Notice.GetProperty("hours").GetInt32());
        Hours.Select(hour => hour.LocalYear).Distinct().Should().Equal(Enumerable.Range(2016, 10));
        Hours.Zip(Hours.Skip(1)).Should().OnlyContain(pair => pair.Second.At - pair.First.At == TimeSpan.FromHours(1));
    }

    [Fact]
    public void Each_wind_rose_share_is_the_one_the_reference_implementation_gives()
    {
        var differing = Differing(WindRoseShares(), ReferenceWindRoseShares());

        differing.Keys.Should().BeEquivalentTo(NamedWindRoseDifferences.Keys, Explained(differing, NamedWindRoseDifferences));
    }

    [Fact]
    public void Each_design_strategy_share_is_the_one_the_reference_implementation_gives()
    {
        var reference = Notice.GetProperty("referenceFigures").GetProperty("designStrategyGroups").EnumerateObject()
            .ToDictionary(share => share.Name, share => share.Value.GetDouble());

        var differing = Differing(DesignStrategyShares(), reference);

        differing.Keys.Should().BeEquivalentTo(NamedDesignStrategyDifferences.Keys, Explained(differing, NamedDesignStrategyDifferences));
    }

    private static Dictionary<string, double> WindRoseShares()
    {
        var sectors = Enumerable.Range(0, 16).Select(at => new WindSector(Guid.NewGuid(), at * 22.5)).ToList();
        var classes = SpeedClassBounds.Select(bounds => new WindSpeedBand(Guid.NewGuid(), bounds.AtLeast, bounds.Below)).ToList();
        var cells = new Dictionary<(Guid Sector, Guid Band), string>();
        foreach (var sector in sectors)
            foreach (var (speedClass, name) in classes.Zip(SpeedClasses))
                cells[(sector.Id, speedClass.Id)] = $"{sector.CentreDegrees}° {name}";
        var names = cells.ToDictionary(cell => Guid.NewGuid(), cell => cell.Value);
        var rose = new WindRose(sectors, classes, names.ToDictionary(named => cells.Single(cell => cell.Value == named.Value).Key, named => named.Key));

        var counted = Hours.Select(hour => rose.CellOf(hour.WindDegrees, hour.WindMetresPerSecond)).OfType<Guid>().Select(cell => names[cell]).ToList();
        return counted.GroupBy(name => name).ToDictionary(cell => cell.Key, cell => 100.0 * cell.Count() / counted.Count);
    }

    private static Dictionary<string, double> ReferenceWindRoseShares()
    {
        var shares = new Dictionary<string, double>();
        foreach (var sector in Notice.GetProperty("referenceFigures").GetProperty("windRose").EnumerateArray())
        {
            var centre = sector.GetProperty("centreDegrees").GetDouble();
            foreach (var share in sector.GetProperty("percentByClass").EnumerateObject())
                shares[$"{centre}° {share.Name}"] = share.Value.GetDouble();
        }
        return shares;
    }

    // As the platform's yearly reduction does: each year's share of the hours a group takes, then the mean of the years.
    private static Dictionary<string, double> DesignStrategyShares()
    {
        var pressure = Psychrometrics.PressureAtElevationKilopascals(Notice.GetProperty("climatePoint").GetProperty("elevationMetres").GetDouble());
        var strategies = new DesignStrategies(new StandardComfort(), StatedComfort.Assumptions with { PressureKilopascals = pressure });
        var groups = TemplateGroups.Groups();

        var years = Hours.GroupBy(hour => hour.LocalYear).Select(year =>
        {
            var taken = year.Select(hour => groups.Of(new HourTests(strategies, hour.AirCelsius, hour.RelativeHumidityPercent))?.Name)
                .OfType<string>().ToList();
            return taken.GroupBy(name => name).ToDictionary(group => group.Key, group => 100.0 * group.Count() / taken.Count);
        }).ToList();
        return groups.Tried.Select(group => group.Name!).ToDictionary(name => name, name => years.Average(year => year.GetValueOrDefault(name)));
    }

    private static Dictionary<string, (double Ours, double Reference)> Differing(
        IReadOnlyDictionary<string, double> ours, IReadOnlyDictionary<string, double> reference) =>
        reference.Where(figure => Math.Abs(ours.GetValueOrDefault(figure.Key) - figure.Value) > ReferenceRounding + 1e-9)
            .ToDictionary(figure => figure.Key, figure => (ours.GetValueOrDefault(figure.Key), figure.Value));

    private static string Explained(Dictionary<string, (double Ours, double Reference)> differing, Dictionary<string, string> named) =>
        "differing: " + string.Join("; ", differing.OrderBy(figure => figure.Key, StringComparer.Ordinal).Select(figure =>
            $"{figure.Key} {figure.Value.Ours:0.000} against {figure.Value.Reference:0.0} ({named.GetValueOrDefault(figure.Key, "not named")})"));

    private static List<RecordedHour> ReadHours()
    {
        var offset = TimeSpan.FromSeconds(Notice.GetProperty("utcOffsetSeconds").GetInt32());
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Recorded", "climate-point-hourly.csv.gz"));
        using var reader = new StreamReader(new GZipStream(file, CompressionMode.Decompress));
        reader.ReadLine();
        var hours = new List<RecordedHour>();
        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split(',');
            var local = DateTime.ParseExact(cells[0], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
            hours.Add(new RecordedHour(
                DateTime.SpecifyKind(local - offset, DateTimeKind.Utc), local.Year,
                Number(cells[1]), Number(cells[2]), Number(cells[3]) / 3.6, Number(cells[4])));
        }
        return hours;
    }

    private static double Number(string written) => double.Parse(written, CultureInfo.InvariantCulture);
}
