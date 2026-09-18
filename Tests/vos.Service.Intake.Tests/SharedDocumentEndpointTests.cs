using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

// Task #7059 — a file a submitter shares about their land. The bytes are kept under a folder beside
// this service, keyed by submission; the model gets the Thing it declares for a shared file, related
// to the submission's project through the predicate the model marks; and the files of a submission
// the model no longer holds are taken out with it.
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

    /// <summary>The submissions model with what the land-intake template declares for a shared file, and
    /// one file the project already shares.</summary>
    private static BrokerSnapshot ModelWithDocuments() =>
        BrokerSnapshot.WithTwoSubmissions()
            .Thing(DocumentArchetype, "SharedDocument", isArchetype: true,
                properties: new Dictionary<string, object> { [SharedDocumentReader.ArchetypeFlag] = new { typeInfo = "vos.Boolean", value = true } })
            .Thing(SharesPredicate, "shares",
                properties: new Dictionary<string, object> { [SharedDocumentReader.PredicateFlag] = new { typeInfo = "vos.Boolean", value = true } })
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

    private sealed class Broker(BrokerSnapshot model)
    {
        public List<string> Fragments { get; } = [];

        public HttpResponseMessage Answer(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (ModelStub.IsFragment(request))
            {
                Fragments.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (request.Method == HttpMethod.Post && path == "/api/subscriptions")
                return ModelStub.Json(Reading(request));
            if (path.EndsWith("/ranges", StringComparison.Ordinal))
                return ModelStub.Json("""{"ThingId":"x","ThingName":"Study","OwnRanges":[],"InheritedRanges":[]}""");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        private string Reading(HttpRequestMessage request)
        {
            var asked = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var selector = JsonDocument.Parse(asked);
            var walks = selector.RootElement.TryGetProperty("traverse", out var traverse)
                        && traverse.ValueKind == JsonValueKind.Array && traverse.GetArrayLength() > 0;
            // The fixture's rooted walk follows the page's own predicates; the listing walks `shares`, which
            // the whole model answers as well as a rooted one would.
            var listing = walks && traverse[0].GetProperty("predicate").GetString() == "shares";
            return walks && !listing ? model.OpenedReaching(Guid.Parse(selector.RootElement.GetProperty("ids")[0].GetString()!)) : model.Opened();
        }
    }

    private (IntakeWebApplicationFactory Factory, Broker Broker) Holding(BrokerSnapshot? model = null, bool runsTheReclaimPass = true)
    {
        var broker = new Broker(model ?? ModelWithDocuments());
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
        string contentType = "application/pdf", string description = "The dolomite and granite line")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(description), "description");
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
}
