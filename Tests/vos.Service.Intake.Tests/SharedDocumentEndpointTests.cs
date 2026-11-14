using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Services;
using vos.Service.Intake.Models;
using vos.Service.Shared.Roles;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Intake.Tests;

// A file a submitter shares about their land. The bytes are kept under a folder beside
// this service, keyed by submission; the model gets the Thing it declares for a shared file, related
// to the submission's project through the predicate the model binds to the shares role; and the files of
// a submission the model no longer holds are taken out with it.
public sealed class SharedDocumentEndpointTests : IDisposable
{
    private const string TicketHeader = "X-Submission-Ticket";
    private static readonly string ThisAddress = BrokerSnapshot.AddressOn("Willow Bend");
    private static readonly string OtherAddress = BrokerSnapshot.AddressOn("Alder Rise");
    private static readonly Guid DocumentArchetype = Guid.Parse("00000000-0000-0000-0000-00000000d0c0");
    private static readonly Guid SharesPredicate = Guid.Parse("00000000-0000-0000-0000-00000000d0c1");
    private static readonly Guid AnEarlierDocument = Guid.Parse("00000000-0000-0000-0000-00000000d0c2");
    private static readonly Guid ThisProject = StableIdentity.Derive(WillowBend.SubmissionId, "project");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "intake-documents-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    // The submissions model with what the land-intake template declares for a shared file, and
    // one file the project already shares.
    private static BrokerSnapshot ModelWithDocuments() =>
        BrokerSnapshot.WithTwoSubmissions()
            .Thing(DocumentArchetype, "SharedDocument", isArchetype: true)
            .Thing(SharesPredicate, "shares")
            .Bound(SharedDocumentReader.ArchetypeRole, DocumentArchetype)
            .Bound(SharedDocumentReader.PredicateRole, SharesPredicate)
            .Thing(ThisProject, "Willow Bend project")
            .Thing(StableIdentity.Derive(WillowBend.SubmissionId, SubmissionFragmentComposer.SubmissionRole), "Willow Bend Submission")
            .Thing(AnEarlierDocument, "survey.pdf", properties: new Dictionary<string, object>
            {
                [SharedDocumentReader.FileNameProperty] = new { typeInfo = "vos.String", value = "survey.pdf" },
                [SharedDocumentReader.DescriptionProperty] = new { typeInfo = "vos.String", value = "The topographical survey" },
                [SharedDocumentReader.ContentTypeProperty] = new { typeInfo = "vos.String", value = "application/pdf" },
                [SharedDocumentReader.SizeBytesProperty] = new { typeInfo = "vos.LongInteger", value = 4096 },
                [SharedDocumentReader.SharedAtProperty] = new { typeInfo = "vos.DateTime", value = "2026-09-18T10:00:00.0000000Z" },
            })
            .Relate(AnEarlierDocument, BrokerSnapshot.IsPredicate, DocumentArchetype)
            .Relate(ThisProject, SharesPredicate, AnEarlierDocument);

    private static readonly Guid FeatureArchetype = Guid.Parse("00000000-0000-0000-0000-00000000fea0");
    private static readonly Guid KindArchetype = Guid.Parse("00000000-0000-0000-0000-00000000fea1");
    private static readonly Guid OfKindPredicate = Guid.Parse("00000000-0000-0000-0000-00000000fea2");
    private static readonly Guid SourcedFromPredicate = Guid.Parse("00000000-0000-0000-0000-00000000fea3");
    private static readonly Guid BrookKind = Guid.Parse("00000000-0000-0000-0000-00000000fea4");
    private static readonly Guid FloodKind = Guid.Parse("00000000-0000-0000-0000-00000000fea5");
    private static readonly Guid ThisSite = StableIdentity.Derive(WillowBend.SubmissionId, SubmissionFragmentComposer.SiteRole);

    private static BrokerSnapshot WithLayers(BrokerSnapshot model) =>
        model
            .Thing(FeatureArchetype, "SiteLayerFeature", isArchetype: true)
            .Thing(KindArchetype, "SiteLayerKind", isArchetype: true)
            .Thing(OfKindPredicate, "ofKind")
            .Thing(SourcedFromPredicate, "sourcedFrom")
            .Thing(BrookKind, "brook-or-wadi")
            .Thing(FloodKind, "flood-extent")
            .Relate(BrookKind, BrokerSnapshot.IsPredicate, KindArchetype)
            .Relate(FloodKind, BrokerSnapshot.IsPredicate, KindArchetype)
            .Bound(LandIntakeRoles.SiteLayerFeature, FeatureArchetype)
            .Bound(LandIntakeRoles.SiteLayerKind, KindArchetype)
            .Bound(LandIntakeRoles.OfLayerKind, OfKindPredicate)
            .Bound(LandIntakeRoles.SourcedFrom, SourcedFromPredicate);

    private const string LayerStoredAs = "0f9e8d7c6b5a49382716051423324150.kml";
    private static readonly Guid ALayerDocument = Guid.Parse("00000000-0000-0000-0000-00000000d0c3");
    private static readonly Guid FirstFeature = Guid.Parse("00000000-0000-0000-0000-00000000fe01");
    private static readonly Guid SecondFeature = Guid.Parse("00000000-0000-0000-0000-00000000fe02");
    private static readonly Guid NotAFeature = Guid.Parse("00000000-0000-0000-0000-00000000fe03");

    private static BrokerSnapshot WithALayerShared(BrokerSnapshot model) =>
        model
            .Thing(ALayerDocument, "layers.kml", properties: new Dictionary<string, object>
            {
                [SharedDocumentReader.FileNameProperty] = new { typeInfo = "vos.String", value = "layers.kml" },
                [SharedDocumentReader.StoredAsProperty] = new { typeInfo = "vos.String", value = LayerStoredAs },
            })
            .Relate(ALayerDocument, BrokerSnapshot.IsPredicate, DocumentArchetype)
            .Relate(ThisProject, SharesPredicate, ALayerDocument)
            .Thing(FirstFeature, "Mill Brook")
            .Thing(SecondFeature, "Flood Line")
            .Thing(NotAFeature, "A note about the file")
            .Relate(FirstFeature, BrokerSnapshot.IsPredicate, FeatureArchetype)
            .Relate(SecondFeature, BrokerSnapshot.IsPredicate, FeatureArchetype)
            .Relate(FirstFeature, SourcedFromPredicate, ALayerDocument)
            .Relate(SecondFeature, SourcedFromPredicate, ALayerDocument)
            .Relate(NotAFeature, SourcedFromPredicate, ALayerDocument);

    private sealed class Broker(BrokerSnapshot model)
    {
        public List<string> Fragments { get; } = [];
        public List<Guid> Removed { get; } = [];
        public Guid? RefusesToRemove { get; init; }
        public Guid? AlreadyGone { get; init; }

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Delete && path.StartsWith("/api/things/", StringComparison.Ordinal))
            {
                var removing = Guid.Parse(path["/api/things/".Length..]);
                if (removing == RefusesToRemove) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                if (removing == AlreadyGone) return new HttpResponseMessage(HttpStatusCode.NotFound);
                Removed.Add(removing);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (ModelStub.IsFragment(request))
            {
                Fragments.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (ModelStub.IsDeclarationRead(request))
                return ModelStub.Json(Reading(request));
            if (path.EndsWith("/ranges", StringComparison.Ordinal))
                return ModelStub.Json("""{"ThingId":"x","ThingName":"Study","OwnRanges":[],"InheritedRanges":[]}""");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private string Reading(HttpRequestMessage request)
        {
            var asked = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var selector = JsonDocument.Parse(asked);
            // The fixture's rooted walk follows the page's own predicates; the listing walks `shares` and the
            // features read walks `sourcedFrom` by its role, which the whole model answers as well as a rooted one would.
            var listing = selector.RootElement.TryGetProperty("traverse", out var traverse)
                          && traverse.ValueKind == JsonValueKind.Array && traverse.GetArrayLength() > 0
                          && (traverse[0].GetProperty("predicate").GetString() == "shares"
                              || traverse[0].GetProperty("predicateRole").ValueKind == JsonValueKind.String);
            return !listing && BrokerSnapshot.RootedAt(selector.RootElement) is { } site ? model.OpenedReaching(site) : model.Opened();
        }
    }

    private (IntakeWebApplicationFactory Factory, Broker Broker) Holding(
        BrokerSnapshot? model = null, bool runsTheReclaimPass = true, Guid? refusesToRemove = null, Guid? alreadyGone = null)
    {
        var broker = new Broker(model ?? ModelWithDocuments()) { RefusesToRemove = refusesToRemove, AlreadyGone = alreadyGone };
        var factory = new IntakeWebApplicationFactory
        {
            HandlerCallback = broker.Answer,
            DocumentDirectory = _folder,
            RunsTheReclaimPass = runsTheReclaimPass,
        };
        return (factory, broker);
    }

    private static async Task<string> TicketFor(IntakeWebApplicationFactory factory, HttpClient client, string emailAddress)
    {
        (await client.PostAsJsonAsync("/submissions/verification", new { emailAddress })).EnsureSuccessStatusCode();
        var exchanged = await client.PostAsJsonAsync(
            "/submissions/ticket", new { emailAddress, code = factory.Mailer.CodeSentTo(emailAddress) });
        exchanged.EnsureSuccessStatusCode();
        return (await exchanged.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ticket").GetString()!;
    }

    private static async Task<HttpResponseMessage> ShareAsync(
        HttpClient client, string submissionId, string? ticket, byte[] bytes, string fileName = "sensitivity map.pdf",
        string contentType = "application/pdf", string description = "The dolomite and granite line",
        string? features = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(description), "description");
        if (features is not null) form.Add(new StringContent(features), LayerFeatureChoice.FormField);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/submissions/{submissionId}/documents") { Content = form };
        if (ticket is not null) request.Headers.Add(TicketHeader, ticket);
        return await client.SendAsync(request);
    }

    private static readonly byte[] SomeBytes = Encoding.ASCII.GetBytes("%PDF-1.4 not really");

    [Fact]
    public async Task A_shared_file_is_kept_beside_the_service_and_the_model_gets_the_thing_it_declares_for_it()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, ThisAddress);

        var response = await ShareAsync(client, WillowBend.SubmissionId, ticket, SomeBytes);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var answered = await response.Content.ReadFromJsonAsync<JsonElement>();
        answered.GetProperty("fileName").GetString().Should().Be("sensitivity map.pdf");
        answered.GetProperty("sizeBytes").GetInt64().Should().Be(SomeBytes.Length);

        var kept = Directory.GetFiles(Path.Combine(_folder, WillowBend.SubmissionId));
        kept.Should().HaveCount(1);
        (await File.ReadAllBytesAsync(kept[0])).Should().Equal(SomeBytes);
        Path.GetFileName(kept[0]).Should().EndWith(".pdf").And.NotContain("sensitivity", "the file is kept under a key of the store's own");

        var fragment = JsonDocument.Parse(broker.Fragments.Single()).RootElement;
        var thing = fragment.GetProperty("Things")[0];
        thing.GetProperty("Properties").GetProperty("fileName").GetProperty("value").GetString().Should().Be("sensitivity map.pdf");
        thing.GetProperty("Properties").GetProperty("description").GetProperty("value").GetString().Should().Be("The dolomite and granite line");
        thing.GetProperty("Properties").GetProperty("contentType").GetProperty("value").GetString().Should().Be("application/pdf");
        thing.GetProperty("Properties").GetProperty("storedAs").GetProperty("value").GetString().Should().Be(Path.GetFileName(kept[0]));
        var edges = fragment.GetProperty("Relationships").EnumerateArray().ToList();
        edges.Should().ContainSingle(edge => edge.GetProperty("Predicate").GetGuid() == BrokerSnapshot.IsPredicate
                                             && edge.GetProperty("Target").GetGuid() == DocumentArchetype);
        edges.Should().ContainSingle(edge => edge.GetProperty("Subject").GetGuid() == ThisProject
                                             && edge.GetProperty("Predicate").GetGuid() == SharesPredicate);
        response.Headers.GetValues(TicketHeader).Single().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_saying_the_limit_and_nothing_is_kept()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, ThisAddress);

        var response = await ShareAsync(client, WillowBend.SubmissionId, ticket, new byte[SharedDocumentService.MaximumFileBytes + 1]);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadAsStringAsync()).Should().Contain("25 MB");
        var (code, values) = await RefusalReading.ReadAsync(response);
        code.Should().Be(RefusalCode.FileTooLarge);
        values.GetProperty("megabytes").GetInt32().Should().Be(25);
        Directory.Exists(Path.Combine(_folder, WillowBend.SubmissionId)).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_ticket_for_another_address_shares_nothing_and_is_told_the_same_as_a_stranger()
    {
        var (factory, broker) = Holding();
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await ShareAsync(client, WillowBend.SubmissionId, await TicketFor(factory, client, OtherAddress), SomeBytes);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(SubmissionFindingsService.NotYourSubmission);
        Directory.Exists(_folder).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_ticket_no_file_is_taken()
    {
        var (factory, _) = Holding();
        await using var __ = factory;
        using var client = factory.CreateClient();

        (await ShareAsync(client, WillowBend.SubmissionId, null, SomeBytes)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // A model that declares no shared file has no place for one; the submitter is told so rather than a
    // file being kept that nothing can list.
    [Fact]
    public async Task A_model_declaring_no_shared_file_takes_none()
    {
        var (factory, broker) = Holding(BrokerSnapshot.WithTwoSubmissions());
        await using var _ = factory;
        using var client = factory.CreateClient();

        var response = await ShareAsync(client, WillowBend.SubmissionId, await TicketFor(factory, client, ThisAddress), SomeBytes);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not taken here");
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_submitter_lists_the_files_their_project_shares()
    {
        var (factory, _) = Holding();
        await using var __ = factory;
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/submissions/{WillowBend.SubmissionId}/documents");
        request.Headers.Add(TicketHeader, await TicketFor(factory, client, ThisAddress));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documents");
        listed.GetArrayLength().Should().Be(1);
        listed[0].GetProperty("fileName").GetString().Should().Be("survey.pdf");
        listed[0].GetProperty("description").GetString().Should().Be("The topographical survey");
        listed[0].GetProperty("sizeBytes").GetInt64().Should().Be(4096);
    }

    // The retention pass prunes a rejected submission's Things once its period has run; the bytes never
    // were the model's, so the store asks which submissions still stand and forgets the rest.
    [Fact]
    public async Task The_files_of_a_submission_the_model_no_longer_holds_are_taken_out_with_it()
    {
        var (factory, _) = Holding(runsTheReclaimPass: false);
        await using var __ = factory;
        Directory.CreateDirectory(Path.Combine(_folder, WillowBend.SubmissionId));
        await File.WriteAllBytesAsync(Path.Combine(_folder, WillowBend.SubmissionId, "kept.pdf"), SomeBytes);
        const string gone = "0b7d2c4e-5f61-4a8e-9c3d-1e2f3a4b5c6d";
        Directory.CreateDirectory(Path.Combine(_folder, gone));
        await File.WriteAllBytesAsync(Path.Combine(_folder, gone, "stale.pdf"), SomeBytes);

        var documents = factory.Services.GetRequiredService<SharedDocumentService>();
        var forgotten = await documents.ReclaimAsync(CancellationToken.None);

        forgotten.Should().Be(1);
        Directory.Exists(Path.Combine(_folder, gone)).Should().BeFalse();
        File.Exists(Path.Combine(_folder, WillowBend.SubmissionId, "kept.pdf")).Should().BeTrue();
    }

    // The pass forgets what the model does not answer for, so a model that declares no shared file — one
    // not seeded yet, or not the one the files were taken into — must answer for nothing rather than
    // read as having let every submission go.
    [Fact]
    public async Task A_model_declaring_no_shared_file_reclaims_nothing()
    {
        var (factory, _) = Holding(BrokerSnapshot.WithTwoSubmissions(), runsTheReclaimPass: false);
        await using var __ = factory;
        Directory.CreateDirectory(Path.Combine(_folder, WillowBend.SubmissionId));
        await File.WriteAllBytesAsync(Path.Combine(_folder, WillowBend.SubmissionId, "kept.pdf"), SomeBytes);

        var documents = factory.Services.GetRequiredService<SharedDocumentService>();
        var forgotten = await documents.ReclaimAsync(CancellationToken.None);

        forgotten.Should().Be(0);
        File.Exists(Path.Combine(_folder, WillowBend.SubmissionId, "kept.pdf")).Should().BeTrue();
    }

    // Read as the host stopping, the client's own timeout once ended the hourly pass for good.
    [Fact]
    public async Task The_hourly_pass_survives_a_broker_slower_than_the_clients_own_timeout()
    {
        var (factory, _) = Holding(runsTheReclaimPass: false);
        await using var __ = factory;
        Directory.CreateDirectory(Path.Combine(_folder, WillowBend.SubmissionId));
        await File.WriteAllBytesAsync(Path.Combine(_folder, WillowBend.SubmissionId, "kept.pdf"), SomeBytes);
        factory.HandlerCallback = _ => throw ElapsedClientTimeout.New();
        var logger = new CapturingLogger<DocumentReclaimService>();
        var pass = new DocumentReclaimService(factory.Services.GetRequiredService<SharedDocumentService>(), logger);

        var started = pass.StartAsync(CancellationToken.None);
        await Settle.UntilAsync(() => logger.Lines.Count > 0 || pass.ExecuteTask!.IsCompleted,
            "the pass either logged the failed hour or ended");

        pass.ExecuteTask!.IsCompleted.Should().BeFalse("the pass waits for the next hour rather than ending");
        logger.Lines.Should().ContainSingle(line => line.Contains("could not be reclaimed this hour"));
        File.Exists(Path.Combine(_folder, WillowBend.SubmissionId, "kept.pdf")).Should().BeTrue();

        await started;
        await pass.StopAsync(CancellationToken.None);
    }

    private static readonly byte[] TwoFeatures = Encoding.UTF8.GetBytes("""
        <kml xmlns="http://www.opengis.net/kml/2.2"><Document>
          <Placemark><name>Mill Brook</name><LineString><coordinates>8.1,47.2 8.2,47.3</coordinates></LineString></Placemark>
          <Placemark><name>Flood Line</name><Polygon><outerBoundaryIs><LinearRing>
            <coordinates>8.1,47.2 8.2,47.2 8.2,47.3 8.1,47.2</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark>
          <Placemark><name>Not Chosen</name><LineString><coordinates>9.1,47.2 9.2,47.3</coordinates></LineString></Placemark>
        </Document></kml>
        """);

    private const string BothChosen = """{"Mill Brook":"brook-or-wadi","Flood Line":"flood-extent"}""";

    private async Task<(HttpResponseMessage Response, Broker Broker)> ShareLayerAsync(
        string features, BrokerSnapshot? model = null, byte[]? bytes = null, string fileName = "layers.kml")
    {
        var (factory, broker) = Holding(model ?? WithLayers(ModelWithDocuments()));
        await using var _ = factory;
        using var client = factory.CreateClient();
        var ticket = await TicketFor(factory, client, ThisAddress);
        return (await ShareAsync(client, WillowBend.SubmissionId, ticket, bytes ?? TwoFeatures, fileName,
            "application/vnd.google-earth.kml+xml", "Layers from the survey", features), broker);
    }

    private static List<JsonElement> Edges(JsonElement fragment) =>
        [.. fragment.GetProperty("Relationships").EnumerateArray()];

    private static bool Relates(List<JsonElement> edges, Guid subject, Guid predicate, Guid target) =>
        edges.Any(edge => edge.GetProperty("Subject").GetGuid() == subject
                          && edge.GetProperty("Predicate").GetGuid() == predicate
                          && edge.GetProperty("Target").GetGuid() == target);

    [Fact]
    public async Task A_layer_file_is_stored_in_one_write_with_a_feature_for_each_chosen_shape_under_the_site()
    {
        var (response, broker) = await ShareLayerAsync(BothChosen);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var fragment = JsonDocument.Parse(broker.Fragments.Single()).RootElement;
        var things = fragment.GetProperty("Things").EnumerateArray().ToList();
        var document = things[0].GetProperty("Id").GetGuid();
        var features = things.Skip(1).ToList();
        features.Select(thing => thing.GetProperty("Name").GetString()).Should().Equal("Mill Brook", "Flood Line");
        JsonDocument.Parse(features[0].GetProperty("Properties").GetProperty("geometry").GetProperty("value").GetString()!)
            .RootElement.GetProperty("type").GetString().Should().Be("LineString");
        features[1].GetProperty("Properties").GetProperty("geometry").GetProperty("typeInfo").GetString().Should().Be("vos.GeoJson");

        var edges = Edges(fragment);
        var kinds = new[] { BrookKind, FloodKind };
        foreach (var (feature, kind) in features.Select(thing => thing.GetProperty("Id").GetGuid()).Zip(kinds))
        {
            Relates(edges, feature, BrokerSnapshot.IsPredicate, FeatureArchetype).Should().BeTrue();
            Relates(edges, ThisSite, BrokerSnapshot.HasPredicate, feature).Should().BeTrue("the site's group is what a clearing takes");
            Relates(edges, feature, OfKindPredicate, kind).Should().BeTrue();
            Relates(edges, feature, SourcedFromPredicate, document).Should().BeTrue();
        }
    }

    [Fact]
    public async Task A_shape_nobody_chose_is_not_read_so_a_broken_one_keeps_no_file_out()
    {
        var withABrokenShape = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(TwoFeatures)
            .Replace("9.1,47.2 9.2,47.3", "east,47.2 9.2,47.3", StringComparison.Ordinal));

        var (response, broker) = await ShareLayerAsync(BothChosen, bytes: withABrokenShape);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonDocument.Parse(broker.Fragments.Single()).RootElement.GetProperty("Things").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task A_layer_file_with_no_kind_chosen_is_stored_as_a_plain_file()
    {
        var (response, broker) = await ShareLayerAsync("{}");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonDocument.Parse(broker.Fragments.Single()).RootElement.GetProperty("Things").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task A_kind_the_model_does_not_declare_is_refused_and_nothing_is_kept()
    {
        var (response, broker) = await ShareLayerAsync("""{"Mill Brook":"lake"}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (code, values) = await RefusalReading.ReadAsync(response);
        code.Should().Be(RefusalCode.TermUnknown);
        values.GetProperty("field").GetString().Should().Be("Mill Brook");
        values.GetProperty("value").GetString().Should().Be("lake");
        Directory.Exists(_folder).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_feature_the_file_does_not_hold_is_refused_and_nothing_is_kept()
    {
        var (response, broker) = await ShareLayerAsync("""{"Upper Catchment":"flood-extent"}""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.FeatureNotInFile);
        Directory.Exists(_folder).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task Kinds_chosen_for_a_file_that_is_no_layer_are_refused_and_nothing_is_kept()
    {
        var (response, broker) = await ShareLayerAsync(BothChosen, bytes: SomeBytes, fileName: "survey.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.LayerFileUnreadable);
        Directory.Exists(_folder).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreadable_choice_of_kinds_is_refused_and_nothing_is_kept()
    {
        var (response, broker) = await ShareLayerAsync("""["Mill Brook"]""");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.FeaturesUnreadable);
        Directory.Exists(_folder).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_declaring_no_site_layer_takes_no_layer_file()
    {
        var (response, broker) = await ShareLayerAsync(BothChosen, model: ModelWithDocuments());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Directory.Exists(_folder).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("siteLayerKind")]
    [InlineData("ofLayerKind")]
    [InlineData("sourcedFrom")]
    public async Task A_model_binding_only_some_of_the_site_layer_roles_takes_no_layer_file(string unbound)
    {
        var role = LandIntakeRoles.All.Single(candidate => candidate.Word == unbound);

        var (response, broker) = await ShareLayerAsync(BothChosen, model: WithLayers(ModelWithDocuments()).Unbound(role));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Directory.Exists(_folder).Should().BeFalse();
        broker.Fragments.Should().BeEmpty();
    }

    private async Task<(HttpResponseMessage Response, Broker Broker)> RemoveAsync(
        Guid documentId, BrokerSnapshot? model = null, string address = "", bool withTicket = true,
        Guid? refusesToRemove = null, Guid? alreadyGone = null)
    {
        var (factory, broker) = Holding(
            model ?? WithALayerShared(WithLayers(ModelWithDocuments())), refusesToRemove: refusesToRemove, alreadyGone: alreadyGone);
        await using var _ = factory;
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/submissions/{WillowBend.SubmissionId}/documents/{documentId}");
        if (withTicket) request.Headers.Add(TicketHeader, await TicketFor(factory, client, address.Length > 0 ? address : ThisAddress));
        return (await client.SendAsync(request), broker);
    }

    private string KeptLayerBytes()
    {
        var path = Path.Combine(_folder, WillowBend.SubmissionId, LayerStoredAs);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TwoFeatures);
        return path;
    }

    [Fact]
    public async Task Removing_a_file_removes_its_features_then_the_file_then_its_bytes()
    {
        var kept = KeptLayerBytes();

        var (response, broker) = await RemoveAsync(ALayerDocument);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        broker.Removed.Should().Equal(FirstFeature, SecondFeature, ALayerDocument);
        File.Exists(kept).Should().BeFalse();
        response.Headers.GetValues(TicketHeader).Single().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Removing_a_file_leaves_what_names_it_as_a_source_without_being_a_feature()
    {
        var (_, broker) = await RemoveAsync(ALayerDocument);

        broker.Removed.Should().NotContain(NotAFeature);
    }

    [Fact]
    public async Task A_file_shared_where_the_model_declares_no_site_layer_is_removed_alone()
    {
        var (response, broker) = await RemoveAsync(AnEarlierDocument, model: ModelWithDocuments());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        broker.Removed.Should().Equal(AnEarlierDocument);
    }

    [Fact]
    public async Task A_thing_the_submission_does_not_share_is_not_removed()
    {
        var (response, broker) = await RemoveAsync(FirstFeature);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.DocumentNotShared);
        broker.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_ticket_for_another_address_removes_nothing_and_is_told_the_same_as_a_stranger()
    {
        var kept = KeptLayerBytes();

        var (response, broker) = await RemoveAsync(ALayerDocument, address: OtherAddress);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RefusalReading.ReadAsync(response)).Code.Should().Be(RefusalCode.NotYourSubmission);
        broker.Removed.Should().BeEmpty();
        File.Exists(kept).Should().BeTrue();
    }

    [Fact]
    public async Task Without_a_ticket_nothing_is_removed()
    {
        var (response, broker) = await RemoveAsync(ALayerDocument, withTicket: false);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        broker.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_removal_that_fails_part_way_keeps_the_file_and_its_bytes_so_it_can_be_asked_again()
    {
        var kept = KeptLayerBytes();

        var (response, broker) = await RemoveAsync(ALayerDocument, refusesToRemove: SecondFeature);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        broker.Removed.Should().Equal(FirstFeature);
        File.Exists(kept).Should().BeTrue();
    }

    [Fact]
    public async Task A_removal_asked_again_after_a_feature_went_carries_on_past_it()
    {
        var kept = KeptLayerBytes();

        var (response, broker) = await RemoveAsync(ALayerDocument, alreadyGone: FirstFeature);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        broker.Removed.Should().Equal(SecondFeature, ALayerDocument);
        File.Exists(kept).Should().BeFalse();
    }
}
