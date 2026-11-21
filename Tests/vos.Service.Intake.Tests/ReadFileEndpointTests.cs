using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Geometry;
using Xunit;

namespace vos.Service.Intake.Tests;

// The page reads a file here before any submission exists to keep it under.
public sealed class ReadFileEndpointTests : IDisposable
{
    private const string Route = "/submissions/read-file";
    private const string Address = "ana.ferreira@example.pt";
    private const string OtherAddress = "joao.silva@example.pt";

    private const string BorderCoordinates = """
        55.55616063595723,25.1548913568011,0 55.54932911385871,25.15129015552567,0 55.5468216600034,25.14978772652261,0
        55.54735722283466,25.14886667062588,0 55.5691377022649,25.15562986578318,0 55.56848828530388,25.15707226778431,0
        55.56296846877041,25.15754361591318,0 55.56259408448088,25.15637327045562,0 55.55932941382773,25.15624859750918,0
        55.55941873916951,25.1551934630042,0 55.55817561038458,25.15471781966161,0 55.55807414688074,25.15484810477037,0
        55.55752719634192,25.15462140660673,0 55.55771982371159,25.15409802690795,0 55.55684163276631,25.15378546087098,0
        55.55616063595723,25.1548913568011,0
        """;

    private static readonly string ReferenceBorderFile = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <kml xmlns="http://www.opengis.net/kml/2.2"><Document>
          <Placemark><name>centre</name><Point><coordinates>55.5515,25.1504,95</coordinates></Point></Placemark>
          <Placemark><name>dubai</name><Polygon><outerBoundaryIs><LinearRing><coordinates>
            {BorderCoordinates}
          </coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
        </Document></kml>
        """;

    private const string SmallSquare = "8.10,47.20 8.11,47.20 8.11,47.21 8.10,47.21 8.10,47.20";
    private const string LargeSquare = "8.20,47.20 8.24,47.20 8.24,47.24 8.20,47.24 8.20,47.20";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "intake-read-file-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private (IntakeWebApplicationFactory Factory, List<HttpRequestMessage> BrokerCalls) Holding(DeclaredModel? model = null)
    {
        var brokerCalls = new List<HttpRequestMessage>();
        var declarations = ModelStub.DeclarationAnswer(model ?? DeclaredModel.Seeded());
        var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = request =>
            {
                lock (brokerCalls) brokerCalls.Add(request);
                return ModelStub.IsDeclarationRead(request) ? ModelStub.Json(declarations) : new HttpResponseMessage(HttpStatusCode.OK);
            },
            DocumentDirectory = _folder,
            RunsTheReclaimPass = false,
        };
        return (factory, brokerCalls);
    }

    private static async Task<string> TicketFor(IntakeWebApplicationFactory factory, HttpClient client, string emailAddress)
    {
        (await client.PostAsJsonAsync("/submissions/verification", new { emailAddress })).EnsureSuccessStatusCode();
        var exchanged = await client.PostAsJsonAsync(
            "/submissions/ticket", new { emailAddress, code = factory.Mailer.CodeSentTo(emailAddress) });
        exchanged.EnsureSuccessStatusCode();
        return (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static async Task<HttpResponseMessage> ReadAsync(
        HttpClient client, string? ticket, byte[] bytes, string fileName, string? emailAddress = Address)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        if (emailAddress is not null) form.Add(new StringContent(emailAddress), "emailAddress");
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = form };
        if (ticket is not null) request.Headers.Add(SubmissionTicket.HeaderName, ticket);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ReadWithATicketAsync(byte[] bytes, string fileName, DeclaredModel? model = null)
    {
        var (factory, _) = Holding(model);
        await using var _factory = factory;
        using var client = factory.CreateClient();
        return await ReadAsync(client, await TicketFor(factory, client, Address), bytes, fileName);
    }

    private async Task<JsonElement> AnswerFor(string text, string fileName) => await AnswerFor(Encoding.UTF8.GetBytes(text), fileName);

    private async Task<JsonElement> AnswerFor(byte[] bytes, string fileName, DeclaredModel? model = null)
    {
        var response = await ReadWithATicketAsync(bytes, fileName, model);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(HttpStatusCode Status, string? Code)> RefusalFor(byte[] bytes, string fileName)
    {
        var response = await ReadWithATicketAsync(bytes, fileName);
        var (code, _) = await RefusalReading.ReadAsync(response);
        return (response.StatusCode, code);
    }

    private static string? Text(JsonElement shape, string property) =>
        shape.GetProperty(property).ValueKind == JsonValueKind.Null ? null : shape.GetProperty(property).GetString();

    private static string GeometryType(JsonElement shape) => shape.GetProperty("geometry").GetProperty("type").GetString()!;

    private static IReadOnlyList<BoundaryPoint> Boundary(JsonElement largest) =>
        largest.GetProperty("boundary").EnumerateArray()
            .Select(corner => Corner(corner.GetProperty("latitude").GetDouble(), corner.GetProperty("longitude").GetDouble()))
            .ToList();

    private static BoundaryPoint Corner(double latitude, double longitude) => new() { Latitude = latitude, Longitude = longitude };

    private static double Measured(IReadOnlyList<BoundaryPoint> boundary, AreaMethod method) =>
        BorderArea.Hectares([.. boundary.Select(corner => (corner.Latitude, corner.Longitude))], method);

    private static byte[] Kmz(string kml)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = new StreamWriter(archive.CreateEntry("doc.kml").Open()))
            entry.Write(kml);
        return buffer.ToArray();
    }

    [Fact]
    public async Task The_reference_border_file_names_its_outline_as_the_largest_area_with_the_hectares_a_submission_measures()
    {
        var answer = await AnswerFor(ReferenceBorderFile, "border.kml");

        var shapes = answer.GetProperty("shapes").EnumerateArray().ToList();
        shapes.Should().ContainSingle("a point is no shape a border or a layer is drawn from");
        Text(shapes[0], "name").Should().Be("dubai");
        GeometryType(shapes[0]).Should().Be("Polygon");

        var largest = answer.GetProperty("largestArea");
        largest.GetProperty("shape").GetInt32().Should().Be(0);
        var boundary = Boundary(largest);
        boundary.Should().HaveCount(15, "the corners are answered without the ring's closing position");
        boundary[0].Should().Be(Corner(25.1548913568011, 55.55616063595723));
        largest.GetProperty("hectares").GetDouble().Should().Be(Measured(boundary, AreaMethod.MeanSphere))
            .And.BeApproximately(62.485, 0.001);
    }

    [Fact]
    public async Task The_largest_area_is_measured_with_the_method_the_model_configures()
    {
        var model = DeclaredModel.Seeded()
            .Unrelate("Parcel", "measuresAreaWith")
            .Relate("Parcel", "measuresAreaWith", DeclaredModel.Wgs84Ellipsoid);

        var largest = (await AnswerFor(Encoding.UTF8.GetBytes(ReferenceBorderFile), "border.kml", model)).GetProperty("largestArea");

        largest.GetProperty("hectares").GetDouble().Should().Be(Measured(Boundary(largest), AreaMethod.Wgs84Ellipsoid))
            .And.BeApproximately(62.348, 0.001);
    }

    [Fact]
    public async Task Every_shape_is_answered_whether_or_not_it_is_named_with_the_folder_and_description_the_file_gives()
    {
        var kml = $"""
            <kml><Document><Folder><name>Parcels</name>
              <Placemark><Polygon><outerBoundaryIs><LinearRing><coordinates>{SmallSquare}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
              <Placemark><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString><name>Mill Brook</name><description>Dry in summer</description></Placemark>
            </Folder></Document></kml>
            """;

        var shapes = (await AnswerFor(kml, "layers.kml")).GetProperty("shapes").EnumerateArray().ToList();

        shapes.Should().HaveCount(2);
        Text(shapes[0], "name").Should().BeNull();
        Text(shapes[0], "folder").Should().Be("Parcels");
        GeometryType(shapes[0]).Should().Be("Polygon");
        Text(shapes[1], "name").Should().Be("Mill Brook", "a name given after the geometry still names the shape");
        Text(shapes[1], "description").Should().Be("Dry in summer");
        Text(shapes[1], "folder").Should().Be("Parcels");
        GeometryType(shapes[1]).Should().Be("LineString");
    }

    [Theory]
    [InlineData("zone.kml")]
    [InlineData("zone.kmz")]
    [InlineData("zone.geojson")]
    [InlineData("walk.gpx")]
    public async Task Each_format_the_intake_reads_is_answered_shape_by_shape(string fileName)
    {
        var kml = $"<kml><Placemark><name>Zone</name><description>Spring</description><Polygon><outerBoundaryIs><LinearRing><coordinates>{SmallSquare}</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark></kml>";
        var (bytes, type) = Path.GetExtension(fileName) switch
        {
            ".kml" => (Encoding.UTF8.GetBytes(kml), "Polygon"),
            ".kmz" => (Kmz(kml), "Polygon"),
            ".geojson" => (Encoding.UTF8.GetBytes("""
                {"type":"FeatureCollection","features":[{"type":"Feature","properties":{"name":"Zone","description":"Spring"},
                 "geometry":{"type":"Polygon","coordinates":[[[8.10,47.20],[8.11,47.20],[8.11,47.21],[8.10,47.20]]]}}]}
                """), "Polygon"),
            _ => (Encoding.UTF8.GetBytes("""<gpx><trk><name>Zone</name><desc>Spring</desc><trkseg><trkpt lat="47.2" lon="8.1"/><trkpt lat="47.3" lon="8.2"/></trkseg></trk></gpx>"""), "LineString"),
        };

        var shape = (await AnswerFor(bytes, fileName)).GetProperty("shapes").EnumerateArray().Should().ContainSingle().Subject;

        Text(shape, "name").Should().Be("Zone");
        Text(shape, "description").Should().Be("Spring");
        Text(shape, "folder").Should().BeNull();
        GeometryType(shape).Should().Be(type);
    }

    [Fact]
    public async Task The_largest_area_is_the_largest_polygon_wherever_it_lies_among_the_shapes()
    {
        const string geojson = """
            {"type":"FeatureCollection","features":[
              {"type":"Feature","properties":{"name":"Small"},"geometry":{"type":"Polygon","coordinates":[[[8.10,47.20],[8.11,47.20],[8.11,47.21],[8.10,47.21],[8.10,47.20]]]}},
              {"type":"Feature","properties":{"name":"Both"},"geometry":{"type":"MultiPolygon","coordinates":[
                [[[8.30,47.20],[8.31,47.20],[8.31,47.21],[8.30,47.20]]],
                [[[8.20,47.20],[8.24,47.20],[8.24,47.24],[8.20,47.24],[8.20,47.20]]]]}}]}
            """;

        var largest = (await AnswerFor(geojson, "parcels.geojson")).GetProperty("largestArea");

        largest.GetProperty("shape").GetInt32().Should().Be(1);
        var boundary = Boundary(largest);
        boundary.Should().HaveCount(4);
        boundary[0].Should().Be(Corner(47.20, 8.20));
        largest.GetProperty("hectares").GetDouble().Should().Be(Measured(boundary, AreaMethod.MeanSphere));
    }

    [Fact]
    public async Task A_polygon_s_hole_does_not_make_its_outline_any_smaller_or_larger()
    {
        var kml = $"""
            <kml><Placemark><name>Zone</name><Polygon>
              <outerBoundaryIs><LinearRing><coordinates>{LargeSquare}</coordinates></LinearRing></outerBoundaryIs>
              <innerBoundaryIs><LinearRing><coordinates>8.21,47.21 8.22,47.21 8.22,47.22 8.21,47.21</coordinates></LinearRing></innerBoundaryIs>
            </Polygon></Placemark></kml>
            """;

        var largest = (await AnswerFor(kml, "zone.kml")).GetProperty("largestArea");

        Boundary(largest).Should().HaveCount(4).And.StartWith(Corner(47.20, 8.20));
    }

    [Fact]
    public async Task A_file_holding_no_polygon_answers_its_shapes_and_names_no_largest_area()
    {
        var answer = await AnswerFor("""<gpx><rte><name>Track</name><rtept lat="47.2" lon="8.1"/><rtept lat="47.3" lon="8.2"/></rte></gpx>""", "walk.gpx");

        answer.GetProperty("shapes").GetArrayLength().Should().Be(1);
        answer.GetProperty("largestArea").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_read_keeps_nothing_and_writes_nothing_to_the_model()
    {
        var (factory, brokerCalls) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, Address);
        var callsBefore = brokerCalls.Count;

        var response = await ReadAsync(client, ticket, Encoding.UTF8.GetBytes(ReferenceBorderFile), "border.kml");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        brokerCalls.Skip(callsBefore).Should().ContainSingle()
            .Which.Should().Match<HttpRequestMessage>(request => ModelStub.IsDeclarationRead(request),
                "the method the area is measured with is the one thing a read asks of the model");
        Directory.Exists(_folder).Should().BeFalse();
    }

    [Fact]
    public async Task A_read_with_a_live_ticket_is_answered_with_a_fresh_one()
    {
        var (factory, _) = Holding();
        await using var _factory = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, Address);

        var response = await ReadAsync(client, ticket, Encoding.UTF8.GetBytes(ReferenceBorderFile), "border.kml");

        var fresh = response.Headers.GetValues(SubmissionTicket.HeaderName).Single();
        fresh.Should().NotBeNullOrEmpty();
        factory.Services.GetRequiredService<SubmissionTicket>().WasIssuedFor(fresh, Address).Should().BeTrue();
    }

    [Fact]
    public async Task Without_a_ticket_nothing_is_read()
    {
        var (factory, _) = Holding();
        await using var _factory = factory;
        using var client = factory.CreateClient();

        var response = await ReadAsync(client, ticket: null, Encoding.UTF8.GetBytes(ReferenceBorderFile), "border.kml");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains(SubmissionTicket.HeaderName).Should().BeFalse();
    }

    [Fact]
    public async Task A_ticket_issued_for_another_address_is_refused()
    {
        var (factory, _) = Holding();
        await using var _factory = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, OtherAddress);

        var response = await ReadAsync(client, ticket, Encoding.UTF8.GetBytes(ReferenceBorderFile), "border.kml");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.AddressNotVerified);
    }

    [Fact]
    public async Task A_model_choosing_no_area_method_is_answered_as_a_service_that_cannot_answer_and_the_reason_is_logged()
    {
        var (factory, _) = Holding(DeclaredModel.Seeded().Unrelate("Parcel", "measuresAreaWith"));
        await using var _factory = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, Address);

        var response = await ReadAsync(client, ticket, Encoding.UTF8.GetBytes(ReferenceBorderFile), "border.kml");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.ServiceUnavailable);
        response.Headers.Contains(SubmissionTicket.HeaderName).Should().BeFalse();
        factory.Log.Lines.Should().Contain(line => line.Contains("measuresAreaWith"));
    }

    [Fact]
    public async Task A_read_naming_no_address_is_refused_saying_so()
    {
        var (factory, _) = Holding();
        await using var _factory = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, Address);

        var response = await ReadAsync(client, ticket, Encoding.UTF8.GetBytes(ReferenceBorderFile), "border.kml", emailAddress: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.EmailAddressMissing);
    }

    [Fact]
    public async Task A_body_that_is_not_a_form_is_refused()
    {
        var (factory, _) = Holding();
        await using var _factory = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, Address);
        using var request = new HttpRequestMessage(HttpMethod.Post, Route) { Content = JsonContent.Create(new { file = "border.kml" }) };
        request.Headers.Add(SubmissionTicket.HeaderName, ticket);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.FileNotAForm);
    }

    [Fact]
    public async Task A_form_carrying_no_file_is_refused()
    {
        (await RefusalFor([], "border.kml")).Should().Be((HttpStatusCode.BadRequest, RefusalCode.NoFile));
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_for_its_size()
    {
        (await RefusalFor(new byte[SharedDocumentService.MaximumFileBytes + 1], "border.kml"))
            .Should().Be((HttpStatusCode.RequestEntityTooLarge, RefusalCode.FileTooLarge));
    }

    [Fact]
    public async Task A_kmz_unpacking_past_the_largest_file_is_refused_for_its_size()
    {
        var unpacked = new string(' ', (int)SharedDocumentService.MaximumFileBytes + 1);

        (await RefusalFor(Kmz($"<kml>{unpacked}</kml>"), "border.kmz")).Should().Be((HttpStatusCode.BadRequest, RefusalCode.FileTooLarge));
    }

    [Fact]
    public async Task A_file_declaring_its_own_entities_is_refused_as_unreadable()
    {
        const string kml = """<?xml version="1.0"?><!DOCTYPE kml [<!ENTITY zone "Zone">]><kml><Placemark><name>&zone;</name></Placemark></kml>""";

        (await RefusalFor(Encoding.UTF8.GetBytes(kml), "border.kml")).Should().Be((HttpStatusCode.BadRequest, RefusalCode.LayerFileUnreadable));
    }

    [Fact]
    public async Task A_file_of_a_kind_the_intake_does_not_read_is_refused_as_unreadable()
    {
        (await RefusalFor(Encoding.UTF8.GetBytes("a,b\n1,2"), "border.csv")).Should().Be((HttpStatusCode.BadRequest, RefusalCode.LayerFileUnreadable));
    }

    [Fact]
    public async Task An_unnamed_shape_past_the_corner_limit_is_refused()
    {
        var corners = string.Join(' ', Enumerable.Range(0, SubmissionLimits.MostBoundaryCorners + 1).Select(index => FormattableString.Invariant(
            $"{8.1 + 0.01 * Math.Cos(2 * Math.PI * index / (SubmissionLimits.MostBoundaryCorners + 1)):F6},{47.2 + 0.01 * Math.Sin(2 * Math.PI * index / (SubmissionLimits.MostBoundaryCorners + 1)):F6}")));
        var kml = $"<kml><Placemark><LinearRing><coordinates>{corners}</coordinates></LinearRing></Placemark></kml>";

        (await RefusalFor(Encoding.UTF8.GetBytes(kml), "border.kml")).Should().Be((HttpStatusCode.BadRequest, RefusalCode.FieldTooMany));
    }
}
