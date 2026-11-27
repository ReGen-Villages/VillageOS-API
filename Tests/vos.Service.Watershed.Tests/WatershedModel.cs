using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Tests;

public sealed class WatershedModel
{
    public static readonly Guid Binds = Guid.NewGuid();
    public static readonly Guid Is = Guid.NewGuid();
    public static readonly Guid Has = Guid.NewGuid();
    public static readonly Guid Studies = Guid.NewGuid();
    public static readonly Guid Assesses = Guid.NewGuid();
    public static readonly Guid ParcelArchetype = Guid.NewGuid();
    public static readonly Guid SlopeClassShareArchetype = Guid.NewGuid();
    public static readonly Guid AspectShareArchetype = Guid.NewGuid();
    public static readonly Guid SiteLayerFeatureArchetype = Guid.NewGuid();
    public static readonly Guid Study = Guid.NewGuid();
    public static readonly Guid Site = Guid.NewGuid();
    public static readonly Guid Parcel = Guid.NewGuid();
    public static readonly Guid HeightsSource = Guid.NewGuid();
    public const string HeightsSourceName = "a heights source";
    public static readonly Guid LandCoverShareArchetype = Guid.NewGuid();
    public static readonly Guid LandCoverSource = Guid.NewGuid();
    public const string LandCoverHost = "tiles.example";
    public const string TileAddress = "https://tiles.example/land-cover/{tile}.tif";
    public static readonly int[] LandCoverCodes = [10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 100];

    public static readonly (double? AtLeast, double? Below)[] SlopeClassBounds = [(0, 2), (2, 5), (5, 10), (10, 20), (20, null)];
    public static readonly double?[] AspectCentres = [0, 45, 90, 135, 180, 225, 270, 315, null];

    public IReadOnlyList<Guid> SlopeClassShares { get; }
    public IReadOnlyList<Guid> AspectShares { get; }
    public IReadOnlyList<Guid> SlopeClassTerms { get; }
    public IReadOnlyList<Guid> AspectTerms { get; }

    private readonly Dictionary<Guid, SnapshotThing> _things = [];
    private readonly List<SnapshotRelationship> _edges = [];

    public WatershedModel(IReadOnlyList<Position>? border, bool bindsAHeightsSource = true)
    {
        foreach (var (id, name) in new[] { (Binds, RoleBindings.BindsPredicateName), (Is, "is"), (Has, "has"), (Studies, "studies"), (Assesses, "assesses") })
            Thing(id, name);
        Thing(ParcelArchetype, "Parcel", isArchetype: true);
        Thing(SlopeClassShareArchetype, "SlopeClassShare", isArchetype: true);
        Thing(AspectShareArchetype, "AspectShare", isArchetype: true);
        Bind(LandIntakeRoles.StudiedSite, Studies);
        Bind(LandIntakeRoles.Assesses, Assesses);
        Bind(LandIntakeRoles.Parcel, ParcelArchetype);
        Bind(LandIntakeRoles.SlopeClassShare, SlopeClassShareArchetype);
        Bind(LandIntakeRoles.AspectShare, AspectShareArchetype);
        Thing(SiteLayerFeatureArchetype, "SiteLayerFeature", isArchetype: true);
        Bind(LandIntakeRoles.SiteLayerFeature, SiteLayerFeatureArchetype);
        Thing(HeightsSource, HeightsSourceName);
        if (bindsAHeightsSource) Bind(LandIntakeRoles.BorderElevation, HeightsSource);

        Thing(Study, "Willow Bend Site Study");
        Thing(Site, "Willow Bend");
        Thing(Parcel, "Willow Bend Parcel-01", properties: border is null ? new() : new()
        {
            ["boundary"] = new(JsonSerializer.SerializeToElement(GeoJsonOf(border)), "vos.GeoJson", "Fact"),
        });
        Relate(Study, Studies, Site);
        Relate(Site, Has, Parcel);
        Relate(Parcel, Is, ParcelArchetype);

        SlopeClassTerms = [.. SlopeClassBounds.Select((bounds, order) => Term($"slope class {order}", new()
        {
            ["atLeastPercent"] = bounds.AtLeast, ["belowPercent"] = bounds.Below,
        }))];
        AspectTerms = [.. AspectCentres.Select((centre, order) => Term($"aspect {order}", new() { ["centreDegrees"] = centre }))];
        SlopeClassShares = [.. SlopeClassTerms.Select(term => Share(SlopeClassShareArchetype, term))];
        AspectShares = [.. AspectTerms.Select(term => Share(AspectShareArchetype, term))];
    }

    public bool DropsObservations { get; set; }

    public HttpStatusCode SnapshotStatus { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode HeightsStatus { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode ObservationsStatus { get; set; } = HttpStatusCode.OK;

    public HttpStatusCode FragmentStatus { get; set; } = HttpStatusCode.OK;

    public static readonly DateTimeOffset ModelNow = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public HttpStatusCode TimeStatus { get; set; } = HttpStatusCode.OK;

    public Func<Position, double> HeightAt { get; set; } = point => 1000 * point.Latitude;

    public List<string> Asked { get; } = [];

    public List<double> Answered { get; } = [];

    public List<(Guid Thing, Dictionary<string, JsonElement> Values)> Observed { get; } = [];

    public List<JsonElement> Fragments { get; } = [];

    public int HeightCalls => Asked.Count(asked => asked.StartsWith("heights", StringComparison.Ordinal));

    public IReadOnlyList<Guid> LandCoverShares { get; private set; } = [];

    public Dictionary<string, byte[]> LandCoverTiles { get; } = [];

    public HttpStatusCode LandCoverStatus { get; set; } = HttpStatusCode.PartialContent;

    public bool LandCoverUnreachable { get; set; }

    public int LandCoverCalls => Asked.Count(asked => asked.StartsWith("land cover", StringComparison.Ordinal));

    public Guid LandCoverShareOf(int code) => LandCoverShares[Array.IndexOf(LandCoverCodes, code)];

    public void CoversLand(bool bindsTheRaster)
    {
        Thing(LandCoverShareArchetype, "LandCoverShare", isArchetype: true);
        Bind(LandIntakeRoles.LandCoverShare, LandCoverShareArchetype);
        Thing(LandCoverSource, "a land cover source", properties: new()
        {
            ["tileAddress"] = new(JsonSerializer.SerializeToElement(TileAddress), "vos.String", "Fact"),
        });
        if (bindsTheRaster) Bind(LandIntakeRoles.LandCoverRaster, LandCoverSource);
        LandCoverShares = [.. LandCoverCodes.Select(code => Share(LandCoverShareArchetype, Term($"land cover {code}", new() { ["worldCoverCode"] = code })))];
    }

    public void Holds(Guid thing, string property, object value) =>
        _things[thing].Properties[property] = new SnapshotProperty(JsonSerializer.SerializeToElement(value), null, "Observation");

    public void StatesBoundary(string stated) =>
        _things[Parcel].Properties["boundary"] = new SnapshotProperty(JsonDocument.Parse(stated).RootElement.Clone(), "vos.GeoJson", "Fact");

    public Guid HoldsAShareOfAnotherKind()
    {
        var archetype = Guid.NewGuid();
        Thing(archetype, "AnotherShare", isArchetype: true);
        return Share(archetype, Term("another term", new() { ["atLeastPercent"] = 0 }));
    }

    public Guid HoldsALayerFeature(string geometry) => HoldsAShape(SiteLayerFeatureArchetype, geometry);

    public Guid HoldsAShapeOfAnotherKind(string geometry)
    {
        var archetype = Guid.NewGuid();
        Thing(archetype, "AnotherShape", isArchetype: true);
        return HoldsAShape(archetype, geometry);
    }

    private Guid HoldsAShape(Guid archetype, string geometry)
    {
        var id = Guid.NewGuid();
        Thing(id, $"Willow Bend shape {id}", properties: new()
        {
            ["geometry"] = new(JsonDocument.Parse(geometry).RootElement.Clone(), "vos.GeoJson", "Fact"),
        });
        Relate(id, Is, archetype);
        Relate(Site, Has, id);
        return id;
    }

    public HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host == LandCoverHost)
            return LandCoverAnswer(Path.GetFileNameWithoutExtension(path), request.Headers.Range!.Ranges.Single());
        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
        if (path == "/api/snapshots")
        {
            Asked.Add("snapshot");
            return SnapshotStatus == HttpStatusCode.OK ? Json(Snapshot()) : new HttpResponseMessage(SnapshotStatus);
        }
        if (path == $"/api/endpoints/{OutsideCalls.FetcherSubdomain}")
        {
            Asked.Add("heights");
            if (HeightsStatus != HttpStatusCode.OK) return new HttpResponseMessage(HeightsStatus);
            var asked = JsonDocument.Parse(body!).RootElement;
            var heights = asked.GetProperty("addressParameters").GetProperty("locations").GetString()!.Split('|')
                .Select(pair => pair.Split(',').Select(part => double.Parse(part, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
                .Select(parts => HeightAt(new Position(parts[0], parts[1]))).ToArray();
            Answered.AddRange(heights);
            return Json(new { results = heights.Select(height => new { elevation = height }), status = "OK" });
        }
        if (path.StartsWith("/api/things/", StringComparison.Ordinal) && path.EndsWith("/observations", StringComparison.Ordinal))
        {
            var thing = Guid.Parse(path.Split('/')[3]);
            Asked.Add($"observations {thing}");
            if (ObservationsStatus != HttpStatusCode.OK) return new HttpResponseMessage(ObservationsStatus) { Content = new StringContent("refused") };
            var values = JsonDocument.Parse(body!).RootElement.EnumerateArray()
                .ToDictionary(item => item.GetProperty("property").GetString()!, item => item.GetProperty("value").Clone());
            Observed.Add((thing, values));
            if (!DropsObservations)
                foreach (var (property, value) in values)
                    _things[thing].Properties[property] = new SnapshotProperty(value, null, "Observation");
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent($$"""{"accepted":{{values.Count}}}""", Encoding.UTF8, "application/json") };
        }
        if (path == "/api/time")
        {
            Asked.Add("time");
            return TimeStatus == HttpStatusCode.OK ? Json(new { now = ModelNow, rate = 60.0 }) : new HttpResponseMessage(TimeStatus);
        }
        if (path == "/api/model/fragment")
        {
            Asked.Add("fragment");
            if (FragmentStatus != HttpStatusCode.OK) return new HttpResponseMessage(FragmentStatus) { Content = new StringContent("refused") };
            Fragments.Add(JsonDocument.Parse(body!).RootElement.Clone());
            return Json(new { });
        }
        throw new InvalidOperationException($"unexpected request to {path}");
    }

    private HttpResponseMessage LandCoverAnswer(string tile, System.Net.Http.Headers.RangeItemHeaderValue range)
    {
        Asked.Add($"land cover {tile} {range.From}-{range.To}");
        if (LandCoverUnreachable) throw new HttpRequestException("the land cover source could not be reached");
        if (LandCoverStatus != HttpStatusCode.PartialContent) return new HttpResponseMessage(LandCoverStatus);
        if (!LandCoverTiles.TryGetValue(tile, out var file)) return new HttpResponseMessage(HttpStatusCode.NotFound);
        var start = (int)Math.Min(range.From!.Value, file.Length);
        var end = (int)Math.Min(range.To!.Value + 1, file.Length);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(file[start..end]) };
    }

    private SnapshotReadResult Snapshot() => new(1, new SnapshotDocument(1, [.. _things.Values], [.. _edges]));

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"),
    };

    private static string GeoJsonOf(IReadOnlyList<Position> corners) => JsonSerializer.Serialize(new
    {
        type = "Polygon",
        coordinates = new[] { corners.Append(corners[0]).Select(corner => new[] { corner.Longitude, corner.Latitude }).ToArray() },
    });

    private Guid Term(string name, Dictionary<string, double?> numbers)
    {
        var id = Guid.NewGuid();
        Thing(id, name, properties: numbers.Where(number => number.Value is not null).ToDictionary(
            number => number.Key, number => new SnapshotProperty(JsonSerializer.SerializeToElement(number.Value!.Value), "vos.Double", "Fact")));
        return id;
    }

    private Guid Share(Guid archetype, Guid term)
    {
        var id = Guid.NewGuid();
        Thing(id, $"Willow Bend share of {_things[term].Name}");
        Relate(id, Is, archetype);
        Relate(id, Assesses, term);
        Relate(Site, Has, id);
        return id;
    }

    private void Thing(Guid id, string name, bool isArchetype = false, Dictionary<string, SnapshotProperty>? properties = null) =>
        _things[id] = new SnapshotThing(id, name, isArchetype, properties ?? [], null, [], null);

    private void Bind(Role role, Guid bound)
    {
        Thing(role.Id, role.Word);
        Relate(role.Id, Binds, bound);
    }

    private void Relate(Guid subject, Guid predicate, Guid target) =>
        _edges.Add(new SnapshotRelationship(Guid.NewGuid(), null, subject, predicate, target, [], null, []));
}
