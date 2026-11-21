using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Geometry;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Service.Transect.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Transect.Tests;

public class TransectNodeTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private static readonly Guid Run = Guid.Parse("86140000-0000-0000-0000-0000000000aa");
    private static readonly Guid Node = Guid.Parse("86140000-0000-0000-0000-0000000000bb");

    private static readonly IReadOnlyList<(double Latitude, double Longitude)> ReferenceBorder =
    [
        (25.1548913568011, 55.55616063595723), (25.15129015552567, 55.54932911385871), (25.14978772652261, 55.5468216600034),
        (25.14886667062588, 55.54735722283466), (25.15562986578318, 55.5691377022649), (25.15707226778431, 55.56848828530388),
        (25.15754361591318, 55.56296846877041), (25.15637327045562, 55.56259408448088), (25.15624859750918, 55.55932941382773),
        (25.1551934630042, 55.55941873916951), (25.15471781966161, 55.55817561038458), (25.15484810477037, 55.55807414688074),
        (25.15462140660673, 55.55752719634192), (25.15409802690795, 55.55771982371159), (25.15378546087098, 55.55684163276631),
    ];

    private static string GeoJsonOf(IReadOnlyList<(double Latitude, double Longitude)> corners) =>
        JsonSerializer.Serialize(new
        {
            type = "Polygon",
            coordinates = new[] { corners.Append(corners[0]).Select(corner => new[] { corner.Longitude, corner.Latitude }).ToArray() },
        });

    // The area method vocabulary as the shared template declares it, with one parcel carrying the given boundary.
    public sealed class Model
    {
        public static readonly Guid Binds = Guid.NewGuid();
        public static readonly Guid ParcelArchetype = Guid.NewGuid();
        public static readonly Guid MeasuresAreaWith = Guid.NewGuid();
        public static readonly Guid AreaMeasuredWith = Guid.NewGuid();
        public static readonly Guid MeanSphere = Guid.NewGuid();
        public static readonly Guid Wgs84Ellipsoid = Guid.NewGuid();
        public static readonly Guid Parcel = Guid.NewGuid();

        private readonly List<SnapshotThing> _things = [];
        private readonly List<SnapshotRelationship> _edges = [];

        public Model(Guid measuredWith, JsonElement? boundary)
        {
            Thing(Binds, RoleBindings.BindsPredicateName);
            Thing(ParcelArchetype, "Parcel", isArchetype: true);
            Thing(MeasuresAreaWith, "measuresAreaWith");
            Thing(AreaMeasuredWith, "areaMeasuredWith");
            Thing(MeanSphere, "Mean sphere");
            Thing(Wgs84Ellipsoid, "WGS84 ellipsoid");
            Bind(LandIntakeRoles.Parcel, ParcelArchetype);
            Bind(LandIntakeRoles.MeasuresAreaWith, MeasuresAreaWith);
            Bind(LandIntakeRoles.AreaMeasuredWith, AreaMeasuredWith);
            Bind(LandIntakeRoles.MeanSphereAreaMethod, MeanSphere);
            Bind(LandIntakeRoles.Wgs84EllipsoidAreaMethod, Wgs84Ellipsoid);
            if (measuredWith != Guid.Empty) Relate(ParcelArchetype, MeasuresAreaWith, measuredWith);
            Thing(Parcel, "Willow Bend Parcel-01", properties: boundary is { } held
                ? new Dictionary<string, SnapshotProperty> { ["boundary"] = new(held, "vos.GeoJson", "Fact") }
                : []);
        }

        public bool HoldsTheParcel { get; init; } = true;

        public string Answer() => JsonSerializer.Serialize(
            new SnapshotReadResult(1, new SnapshotDocument(1, [.. _things.Where(thing => HoldsTheParcel || thing.Id != Parcel)], _edges)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        private void Thing(Guid id, string name, bool isArchetype = false, Dictionary<string, SnapshotProperty>? properties = null) =>
            _things.Add(new SnapshotThing(id, name, isArchetype, properties ?? [], null, [], null));

        private void Bind(Role role, Guid bound)
        {
            Thing(role.Id, role.Word);
            Relate(role.Id, Binds, bound);
        }

        private void Relate(Guid subject, Guid predicate, Guid target) =>
            _edges.Add(new SnapshotRelationship(Guid.NewGuid(), null, subject, predicate, target, [], null, []));
    }

    private sealed class Broker(Func<string> snapshot, HttpStatusCode snapshotStatus = HttpStatusCode.OK)
    {
        public List<JsonElement> Fragments { get; } = [];

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/snapshots")
                return snapshotStatus == HttpStatusCode.OK
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(snapshot(), Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(snapshotStatus);
            if (path == "/api/model/fragment")
            {
                Fragments.Add(JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement.Clone());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
            throw new InvalidOperationException($"unexpected request to {path}");
        }
    }

    private static TransectNode NodeAnsweredBy(Broker broker) =>
        new(new TestHttpClientFactory(new HttpClient(new MockHttpMessageHandler(broker.Answer))),
            NullLogger<TransectNode>.Instance, MyceliumUrl, new LaunchToken("svc-jwt"));

    private static JsonElement Envelope(string subject) =>
        JsonDocument.Parse($$"""{"runId":"{{Run}}","nodeId":"{{Node}}","params":{},"inputs":{"subject":{{subject}}}}""").RootElement;

    private static JsonElement StringValue(string text) => JsonSerializer.SerializeToElement(text);

    private static JsonElement ObjectValue(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task<(NodeResponse Response, Broker Broker)> MeasureAsync(Model model, string? subject = null)
    {
        var broker = new Broker(model.Answer);
        var response = await NodeAnsweredBy(broker).HandleNodeAsync(Envelope(subject ?? $"\"{Model.Parcel}\""));
        return (response, broker);
    }

    private static IEnumerable<(Guid Subject, Guid Predicate, Guid Target)> Edges(JsonElement fragment, string list) =>
        fragment.GetProperty(list).EnumerateArray().Select(edge => (
            edge.GetProperty("Subject").GetGuid(), edge.GetProperty("Predicate").GetGuid(), edge.GetProperty("Target").GetGuid()));

    [Theory]
    [InlineData(AreaMethod.MeanSphere)]
    [InlineData(AreaMethod.Wgs84Ellipsoid)]
    public async Task A_bordered_parcel_is_measured_with_the_method_the_model_configures_and_forgets_the_other(AreaMethod method)
    {
        var (chosen, other) = method == AreaMethod.MeanSphere ? (Model.MeanSphere, Model.Wgs84Ellipsoid) : (Model.Wgs84Ellipsoid, Model.MeanSphere);

        var (response, broker) = await MeasureAsync(new Model(chosen, StringValue(GeoJsonOf(ReferenceBorder))));

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        var fragment = broker.Fragments.Should().ContainSingle().Subject;
        var parcel = fragment.GetProperty("Things").EnumerateArray().Should().ContainSingle(
            "the fragment names the predicates only by identifier, never as Things of their own").Subject;
        parcel.GetProperty("Id").GetGuid().Should().Be(Model.Parcel);
        parcel.GetProperty("Properties").GetProperty("measuredAreaHectares").GetProperty("value").GetDouble()
            .Should().Be(BorderArea.Hectares(ReferenceBorder, method));
        Edges(fragment, "Relationships").Should().Equal((Model.Parcel, Model.AreaMeasuredWith, chosen));
        Edges(fragment, "Retractions").Should().Equal((Model.Parcel, Model.AreaMeasuredWith, other));
    }

    [Theory]
    [InlineData(AreaMethod.MeanSphere, 62.484565)]
    [InlineData(AreaMethod.Wgs84Ellipsoid, 62.347761)]
    public async Task The_reference_border_measures_what_the_intake_measures(AreaMethod method, double hectares)
    {
        var chosen = method == AreaMethod.MeanSphere ? Model.MeanSphere : Model.Wgs84Ellipsoid;

        var (_, broker) = await MeasureAsync(new Model(chosen, StringValue(GeoJsonOf(ReferenceBorder))));

        broker.Fragments.Single().GetProperty("Things")[0].GetProperty("Properties").GetProperty("measuredAreaHectares")
            .GetProperty("value").GetDouble().Should().BeApproximately(hectares, 1e-5);
    }

    [Fact]
    public async Task The_step_answers_the_parcel_it_measured_so_a_later_step_can_be_wired_to_it()
    {
        var (response, _) = await MeasureAsync(new Model(Model.MeanSphere, StringValue(GeoJsonOf(ReferenceBorder))));

        JsonSerializer.SerializeToElement(response.Outputs[TransectNode.SubjectPort]).GetString().Should().Be(Model.Parcel.ToString());
    }

    [Fact]
    public async Task A_subject_naming_the_parcel_by_id_is_measured_too()
    {
        var (response, broker) = await MeasureAsync(
            new Model(Model.MeanSphere, StringValue(GeoJsonOf(ReferenceBorder))), $$"""{"id":"{{Model.Parcel}}"}""");

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        broker.Fragments.Should().ContainSingle();
    }

    [Fact]
    public async Task A_boundary_held_as_a_geojson_object_rather_than_text_is_measured_too()
    {
        var (response, broker) = await MeasureAsync(new Model(Model.MeanSphere, ObjectValue(GeoJsonOf(ReferenceBorder))));

        response.Result.Should().Be(NodeResponse.Succeeded, response.Error);
        broker.Fragments.Should().ContainSingle();
    }

    public static TheoryData<string, Func<Model>, string> Refusals => new()
    {
        { "no subject", () => new Model(Model.MeanSphere, StringValue(GeoJsonOf(ReferenceBorder))), "null" },
        { "a subject that is no identifier", () => new Model(Model.MeanSphere, StringValue(GeoJsonOf(ReferenceBorder))), "\"not-a-thing\"" },
        { "a parcel the model does not hold", () => new Model(Model.MeanSphere, StringValue(GeoJsonOf(ReferenceBorder))) { HoldsTheParcel = false }, "" },
        { "a parcel with no boundary", () => new Model(Model.MeanSphere, null), "" },
        { "a border of two corners", () => new Model(Model.MeanSphere, StringValue(GeoJsonOf([(39.5, 0), (39.5, 0.01)]))), "" },
        { "a boundary that is no polygon", () => new Model(Model.MeanSphere, StringValue("""{"type":"LineString","coordinates":[[0,39.5],[0.01,39.5]]}""")), "" },
        { "a model choosing no area method", () => new Model(Guid.Empty, StringValue(GeoJsonOf(ReferenceBorder))), "" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_call_it_cannot_measure_fails_naming_why_and_writes_nothing(string why, Func<Model> model, string subject)
    {
        var (response, broker) = await MeasureAsync(model(), subject.Length == 0 ? null : subject);

        response.Result.Should().Be(NodeResponse.Failed, why);
        response.Error.Should().NotBeNullOrWhiteSpace();
        response.Retry.Should().BeNull("{0} is not something calling again would change", why);
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_broker_that_cannot_answer_is_a_failure_that_can_be_tried_again()
    {
        var broker = new Broker(() => "{}", HttpStatusCode.ServiceUnavailable);

        var response = await NodeAnsweredBy(broker).HandleNodeAsync(Envelope($"\"{Model.Parcel}\""));

        response.Result.Should().Be(NodeResponse.Failed);
        response.Retry.Should().NotBeNull();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public void The_step_takes_the_parcel_in_and_hands_it_on()
    {
        var node = NodeAnsweredBy(new Broker(() => "{}"));

        node.Ports.Should().BeEquivalentTo(new[]
        {
            PortDescriptor.Input(TransectNode.SubjectPort, "object", required: true),
            PortDescriptor.Output(TransectNode.SubjectPort, "object"),
        });
    }
}
