using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Stomata.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Stomata.Tests;

public class StomataNodeTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly Guid Run = Guid.Parse("81960000-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("81960000-0000-0000-0000-0000000000bb");
    private static readonly DateTime FirstHour = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset WorkedOutAt = new(2031, 6, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly string[] SectorNames =
    [
        "north", "north-northeast", "northeast", "east-northeast", "east", "east-southeast", "southeast", "south-southeast",
        "south", "south-southwest", "southwest", "west-southwest", "west", "west-northwest", "northwest", "north-northwest",
    ];

    private static readonly (string Name, double? AtLeast, double? Below)[] Bands =
    [
        ("under-2-metres-per-second", null, 2.0), ("2-to-3-metres-per-second", 2.0, 3.0), ("3-to-5-metres-per-second", 3.0, 5.0),
        ("5-to-9-metres-per-second", 5.0, 9.0), ("over-9-metres-per-second", 9.0, null),
    ];

    // The study, its site and the wind rose vocabulary as the shared template declares them.
    private sealed class Model
    {
        public static readonly Guid Study = Guid.NewGuid();
        public static readonly Guid Site = Guid.NewGuid();
        private static readonly Guid Binds = Guid.NewGuid();
        private static readonly Guid Is = Guid.NewGuid();
        private static readonly Guid Studies = Guid.NewGuid();
        private static readonly Guid InSector = Guid.NewGuid();
        private static readonly Guid InSpeedClass = Guid.NewGuid();

        private readonly List<SnapshotThing> _things = [];
        private readonly List<SnapshotRelationship> _edges = [];

        public Dictionary<string, Guid> Cells { get; } = [];

        public Model(bool studiesASite = true)
        {
            Thing(Binds, RoleBindings.BindsPredicateName);
            Thing(Is, "is");
            Thing(Studies, "studies");
            Thing(InSector, "inSector");
            Thing(InSpeedClass, "inSpeedClass");
            Bind(LandIntakeRoles.StudiedSite, Studies);
            Thing(Study, "Willow Bend Site Study");
            Thing(Site, "Willow Bend");
            if (studiesASite) Relate(Study, Studies, Site);

            var sectorKind = Kind(StomataRoles.WindDirectionSector, "WindDirectionSector");
            var bandKind = Kind(StomataRoles.WindSpeedClass, "WindSpeedClass");
            var cellKind = Kind(StomataRoles.WindRoseCell, "WindRoseCell");
            var sectors = SectorNames.Select((name, at) => Member(sectorKind, name, ("centreDegrees", at * 22.5))).ToList();
            var bands = Bands.Select(band => Member(bandKind, band.Name,
                [.. new[] { ("atLeastMetresPerSecond", band.AtLeast), ("belowMetresPerSecond", band.Below) }
                    .Where(bound => bound.Item2 is not null).Select(bound => (bound.Item1, bound.Item2!.Value))])).ToList();
            foreach (var (sector, name) in sectors.Zip(SectorNames))
                foreach (var (band, bandName) in bands.Zip(Bands.Select(band => band.Name)))
                {
                    var cell = Member(cellKind, $"{name} {bandName}");
                    Relate(cell, InSector, sector);
                    Relate(cell, InSpeedClass, band);
                    Cells[$"{name} {bandName}"] = cell;
                }
        }

        public string Answer() => JsonSerializer.Serialize(
            new SnapshotReadResult(1, new SnapshotDocument(1, _things, _edges)), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        private Guid Kind(Role role, string name)
        {
            var kind = Guid.NewGuid();
            Thing(kind, name, isArchetype: true);
            Bind(role, kind);
            return kind;
        }

        private Guid Member(Guid kind, string name, params (string Name, double Value)[] properties)
        {
            var member = Guid.NewGuid();
            Thing(member, name, properties: properties.ToDictionary(
                property => property.Name, property => new SnapshotProperty(JsonSerializer.SerializeToElement(property.Value), "vos.Double", null)));
            Relate(member, Is, kind);
            return member;
        }

        private void Bind(Role role, Guid bound)
        {
            Thing(role.Id, role.Word);
            Relate(role.Id, Binds, bound);
        }

        private void Thing(Guid id, string name, bool isArchetype = false, Dictionary<string, SnapshotProperty>? properties = null) =>
            _things.Add(new SnapshotThing(id, name, isArchetype, properties ?? [], null, [], null));

        private void Relate(Guid subject, Guid predicate, Guid target) =>
            _edges.Add(new SnapshotRelationship(Guid.NewGuid(), null, subject, predicate, target, [], null, []));
    }

    private sealed class Broker(Model model)
    {
        public Dictionary<string, List<(DateTime At, double Value)>> Series { get; } = new()
        {
            [StomataNode.TemperatureSeries] = [(FirstHour, 30.0)],
            [StomataNode.RelativeHumiditySeries] = [(FirstHour, 50.0)],
            [StomataNode.WindSpeedSeries] = [(FirstHour, 3.4)],
            [StomataNode.WindDirectionSeries] = [(FirstHour, 100.0)],
            [StomataNode.ParticulateMatterTenSeries] = [(FirstHour, 20.0)],
            [StomataNode.ParticulateMatterTwoAndAHalfSeries] = [(FirstHour, 8.0)],
        };

        public double? ElevationMetres { get; set; } = 0.0;
        public double LimitTen { get; set; } = 45.0;
        public HttpStatusCode DepositStatus { get; set; } = HttpStatusCode.Accepted;

        public List<string> Asked { get; } = [];
        public List<JsonElement> Deposited { get; } = [];
        public List<JsonElement> Fragments { get; } = [];

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            Asked.Add($"{request.Method} {path}");
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            if (path == "/api/snapshots") return Json(model.Answer());
            if (path == $"/api/things/{Model.Study}/properties")
                return Json(JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    [$"SiteStudy.{StomataNode.ParticulateMatterTenLimit}"] = new { Value = LimitTen },
                    [$"SiteStudy.{StomataNode.ParticulateMatterTwoAndAHalfLimit}"] = new { Value = 15.0 },
                }));
            if (path == $"/api/things/{Model.Site}/properties")
                return Json(ElevationMetres is { } metres
                    ? JsonSerializer.Serialize(new Dictionary<string, object> { [StomataNode.ElevationProperty] = new { Value = metres } })
                    : "{}");
            var observations = $"/api/things/{Model.Site}/properties/";
            if (path.StartsWith(observations) && path.EndsWith("/observations"))
            {
                var series = path[observations.Length..^"/observations".Length];
                return Series.TryGetValue(series, out var readings)
                    ? Json(JsonSerializer.Serialize(new { samples = readings.Select(reading => new { value = reading.Value, observedAt = reading.At }) }))
                    : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"Property not found\"}") };
            }
            if (path == "/api/sediment")
            {
                if (DepositStatus != HttpStatusCode.Accepted)
                    return new HttpResponseMessage(DepositStatus) { Content = new StringContent("{\"error\":\"refused\"}") };
                Deposited.AddRange(JsonDocument.Parse(body!).RootElement.EnumerateArray().Select(item => item.Clone()));
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
            if (path == $"/api/things/{Model.Site}/history/changed") return Json("{}");
            if (path == "/api/model/fragment")
            {
                Fragments.Add(JsonDocument.Parse(body!).RootElement.Clone());
                return Json("{}");
            }
            throw new InvalidOperationException($"unexpected request to {path}");
        }

        public IEnumerable<JsonElement> DepositedAs(string property) =>
            Deposited.Where(item => item.GetProperty("property").GetString() == property);

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class StoppedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private static async Task<NodeResponse> WorkOutAsync(Broker broker, string? subject = null) =>
        await new StomataNode(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(broker.Answer)), NullLogger<StomataNode>.Instance, MyceliumUrl,
                new LaunchToken("svc-jwt"), clock: new StoppedClock(WorkedOutAt))
            .HandleNodeAsync(JsonDocument.Parse(
                $$$"""{"runId":"{{{Run}}}","nodeId":"{{{Node}}}","params":{},"inputs":{"subject":{{{subject ?? $"\"{Model.Study}\""}}}}}""").RootElement);

    private static DateTime? Stamp(Broker broker) =>
        broker.Fragments.Select(fragment => fragment.GetProperty("Things")[0])
            .Where(thing => thing.GetProperty("Id").GetGuid() == Model.Study)
            .Select(thing => thing.GetProperty("Properties").GetProperty(StomataNode.StampProperty).GetProperty("value").GetDateTime())
            .Cast<DateTime?>()
            .SingleOrDefault();

    [Fact]
    public async Task An_hour_of_climate_writes_its_wind_rose_cell_humidity_ratio_and_windows_open_on_the_site()
    {
        var model = new Model();
        var broker = new Broker(model);

        (await WorkOutAsync(broker)).Result.Should().Be(NodeResponse.Succeeded);

        broker.Deposited.Should().OnlyContain(item => item.GetProperty("objectId").GetGuid() == Model.Site
                                                      && item.GetProperty("observedAt").GetDateTime() == FirstHour);
        broker.DepositedAs(StomataNode.WindRoseCellReading).Single().GetProperty("value").GetGuid()
            .Should().Be(model.Cells["east 3-to-5-metres-per-second"]);
        broker.DepositedAs(StomataNode.HumidityRatioReading).Single().GetProperty("value").GetDouble().Should().BeApproximately(13.31, 0.01);
        broker.DepositedAs(StomataNode.WindowsOpenReading).Single().GetProperty("value").GetDouble().Should().Be(1.0);
    }

    [Fact]
    public async Task The_stamp_is_written_last_after_every_reading_is_committed_and_the_history_change_is_reported()
    {
        var broker = new Broker(new Model());

        await WorkOutAsync(broker);

        var writes = broker.Asked.Where(asked => asked.StartsWith("POST") && !asked.EndsWith("/api/snapshots")).ToList();
        writes.Should().Equal("POST /api/sediment", $"POST /api/things/{Model.Site}/history/changed", "POST /api/model/fragment");
        Stamp(broker).Should().Be(WorkedOutAt.UtcDateTime);
    }

    // The rose's shares are taken over the hours that have a cell, so a calm hour is left out of every share.
    [Fact]
    public async Task A_calm_hour_writes_no_wind_rose_cell_and_still_writes_the_rest()
    {
        var broker = new Broker(new Model());
        broker.Series[StomataNode.WindSpeedSeries] = [(FirstHour, 0.0)];

        await WorkOutAsync(broker);

        broker.DepositedAs(StomataNode.WindRoseCellReading).Should().BeEmpty();
        broker.DepositedAs(StomataNode.HumidityRatioReading).Should().ContainSingle();
        broker.DepositedAs(StomataNode.WindowsOpenReading).Should().ContainSingle();
    }

    [Theory]
    [InlineData(StomataNode.TemperatureSeries, StomataNode.HumidityRatioReading)]
    [InlineData(StomataNode.RelativeHumiditySeries, StomataNode.HumidityRatioReading)]
    [InlineData(StomataNode.WindSpeedSeries, StomataNode.WindRoseCellReading)]
    [InlineData(StomataNode.WindDirectionSeries, StomataNode.WindRoseCellReading)]
    [InlineData(StomataNode.ParticulateMatterTenSeries, StomataNode.WindowsOpenReading)]
    [InlineData(StomataNode.ParticulateMatterTwoAndAHalfSeries, StomataNode.WindowsOpenReading)]
    public async Task An_hour_missing_one_series_writes_no_reading_that_needs_it_and_every_reading_that_does_not(string missing, string unwritten)
    {
        var broker = new Broker(new Model());
        broker.Series.Remove(missing);

        (await WorkOutAsync(broker)).Result.Should().Be(NodeResponse.Succeeded);

        broker.DepositedAs(unwritten).Should().BeEmpty();
        broker.Deposited.Select(item => item.GetProperty("property").GetString()).Should().HaveCount(2).And.NotContain(unwritten);
        Stamp(broker).Should().NotBeNull();
    }

    [Fact]
    public async Task A_site_with_no_history_at_all_writes_no_reading_and_still_writes_the_stamp()
    {
        var broker = new Broker(new Model());
        broker.Series.Clear();

        (await WorkOutAsync(broker)).Result.Should().Be(NodeResponse.Succeeded);

        broker.Asked.Should().NotContain(asked => asked.EndsWith("/api/sediment") || asked.EndsWith("/history/changed"));
        Stamp(broker).Should().Be(WorkedOutAt.UtcDateTime);
    }

    [Fact]
    public async Task A_site_whose_climate_data_point_has_no_elevation_writes_no_humidity_ratio()
    {
        var broker = new Broker(new Model()) { ElevationMetres = null };

        await WorkOutAsync(broker);

        broker.DepositedAs(StomataNode.HumidityRatioReading).Should().BeEmpty();
        broker.DepositedAs(StomataNode.WindRoseCellReading).Should().ContainSingle();
    }

    [Fact]
    public async Task The_particulate_limits_are_the_studys_own()
    {
        var broker = new Broker(new Model()) { LimitTen = 15.0 };

        await WorkOutAsync(broker);

        broker.DepositedAs(StomataNode.WindowsOpenReading).Single().GetProperty("value").GetDouble().Should().Be(0.0);
    }

    [Fact]
    public async Task A_study_that_studies_no_site_writes_only_the_stamp()
    {
        var broker = new Broker(new Model(studiesASite: false));

        (await WorkOutAsync(broker)).Result.Should().Be(NodeResponse.Succeeded);

        broker.Deposited.Should().BeEmpty();
        Stamp(broker).Should().Be(WorkedOutAt.UtcDateTime);
    }

    [Fact]
    public async Task A_deposit_the_broker_cannot_take_now_fails_as_one_to_try_again_and_writes_no_stamp()
    {
        var broker = new Broker(new Model()) { DepositStatus = HttpStatusCode.ServiceUnavailable };

        var response = await WorkOutAsync(broker);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_deposit_fails_the_node_and_writes_no_stamp()
    {
        var broker = new Broker(new Model()) { DepositStatus = HttpStatusCode.NotFound };

        var response = await WorkOutAsync(broker);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
        broker.Fragments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"not an identifier\"")]
    public async Task A_subject_naming_no_study_fails_the_node(string subject)
    {
        var response = await WorkOutAsync(new Broker(new Model()), subject);

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().BeNull();
    }
}
