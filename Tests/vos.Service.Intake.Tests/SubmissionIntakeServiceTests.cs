using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Intake.Tests.ModelStub;
using vos.Service.Shared;

namespace vos.Service.Intake.Tests;

public class SubmissionIntakeServiceTests
{
    private const string DocumentText = $$"""
        {
          "submissionId": "{{WillowBend.SubmissionId}}",
          "project": { "name": "Willow Bend Regeneration" },
          "contact": { "name": "Ana Ferreira", "emailAddress": "ana.ferreira@example.pt" },
          "site": { "name": "Willow Bend", "statedAreaHectares": 24.0, "population": 320 },
          "parcel": {
            "boundarySource": "generated-from-stated-area",
            "boundary": [
              { "latitude": 39.4988, "longitude": -8.4168 },
              { "latitude": 39.5036, "longitude": -8.4168 },
              { "latitude": 39.5036, "longitude": -8.4106 },
              { "latitude": 39.4988, "longitude": -8.4106 }
            ]
          }
        }
        """;

    // What the route hands this service. Reading a posted document is the route's, because the
    // ticket is checked against the address in it before anything is written.
    private static Submission Document => SubmissionReader.Read(DocumentText);

    private static SubmissionIntakeService Service(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var declared = new DeclaredBroker(SeededModel.Build());
        return ServiceAnswering(new MockHttpMessageHandler(request => declared.Answer(request) ?? respond(request)));
    }

    // For a test whose answers must be able to overlap; a handler answering synchronously runs
    // each call to completion before the next starts, whatever the caller did.
    private static SubmissionIntakeService Service(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var declared = new DeclaredBroker(SeededModel.Build());
        return ServiceAnswering(MockHttpMessageHandler.AnsweringAsynchronously(
            async request => declared.Answer(request) ?? await respond(request)));
    }

    // Built once, so a test can ask the model which Thing a role binds and compare it with what was written.
    private static readonly DeclaredModel SeededModel = DeclaredModel.Seeded();

    private static SubmissionIntakeService ServiceAnswering(HttpMessageHandler answers)
    {
        var mycelium = new IntakeMyceliumClient(
            new PerCallHttpClientFactory(answers),
            NullLogger<IntakeMyceliumClient>.Instance,
            "http://localhost",
            new LaunchToken("test-token"));
        return new SubmissionIntakeService(
            mycelium,
            new HeldDeclarations(mycelium, TimeProvider.System),
            new ModelClock(new FakeTimeProvider(WillowBend.ArrivedAt)));
    }

    // A clock that does not move, so an arrival time can be asserted rather than bounded.
    private sealed class FakeTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    // A broker answering every declaration read with the model a test built, recording what it was asked
    // for; every other call falls through to the responder the test gave.
    private sealed class DeclaredBroker(SnapshotDocument model)
    {
        private static readonly JsonSerializerOptions AsTheBrokerWritesIt = new(JsonSerializerDefaults.Web);

        public SubscriptionSelector? AskedFor { get; private set; }

        public int Reads { get; private set; }

        public HttpResponseMessage? Answer(HttpRequestMessage request)
        {
            if (!IsDeclarationRead(request)) return null;
            Reads++;
            AskedFor = JsonSerializer.Deserialize<SubscriptionSelector>(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(), AsTheBrokerWritesIt);
            return Json(JsonSerializer.Serialize(new SnapshotReadResult(0, model), AsTheBrokerWritesIt));
        }
    }

    // A service reading its declarations out of the model a test built, against a broker that answers
    // everything else the way the responder says.
    private static (SubmissionIntakeService Service, DeclaredBroker Read) ServiceReading(
        DeclaredModel model, Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var read = new DeclaredBroker(model.Build());
        var otherwise = respond ?? Holds;
        return (ServiceAnswering(new MockHttpMessageHandler(request => read.Answer(request) ?? otherwise(request))), read);
    }

    // A wizard posts the whole submission again on every save, and what it is composed against changes only
    // when the model does.
    [Fact]
    public async Task A_second_submission_reads_no_declaration_again()
    {
        var (service, read) = ServiceReading(DeclaredModel.Seeded());
        await service.SubmitAsync(Document, CancellationToken.None);
        read.Reads.Should().Be(2, "the submission's shape and its vocabularies are each one read");

        await service.SubmitAsync(Document, CancellationToken.None);

        read.Reads.Should().Be(2);
    }

    [Fact]
    public async Task A_declaration_read_opens_no_subscription()
    {
        var paths = new List<string>();
        var (service, _) = ServiceReading(DeclaredModel.Seeded(), request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Holds(request);
        });

        await service.SubmitAsync(Document, CancellationToken.None);

        paths.Should().NotContain(path => path.StartsWith("/api/subscriptions", StringComparison.Ordinal));
    }

    // A model seeded with the archetypes but not the vocabularies would take a submission and write the
    // words back with no relationship, which is the state land allocation reads as every allocation uncategorised.
    [Fact]
    public async Task A_model_holding_the_archetypes_but_not_the_vocabularies_is_refused()
    {
        var (service, _) = ServiceReading(DeclaredModel.Seeded().Without("AllocationCategory"));

        var refusal = await Assert.ThrowsAsync<ModelNotSeededError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        refusal.Message.Should().Contain(LandIntakeRoles.AllocationCategory.Word);
    }

    // Both gates refuse an unseeded model. Read after the archetypes rather than beside them, so which
    // refusal a planner sees is settled here rather than by which call answered first.
    [Fact]
    public async Task A_model_missing_everything_is_refused_by_naming_the_archetypes()
    {
        // The vocabulary is missing too, so both gates would refuse. That is what makes the refusal below
        // an ordering test rather than an assertion that only one gate exists.
        var (service, read) = ServiceReading(
            DeclaredModel.Seeded().Without("AllocationCategory").Without("Site"),
            request => IsFragment(request) ? Json("{}") : new HttpResponseMessage(HttpStatusCode.NotFound));

        var refusal = await Assert.ThrowsAsync<ModelNotSeededError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        refusal.Message.Should().Contain(LandIntakeRoles.Site.Word)
            .And.NotContain(LandIntakeRoles.AllocationCategory.Word);
        read.AskedFor!.Ids.Should().NotContain(LandIntakeRoles.AllocationCategory.Id,
            "the archetype gate refuses before the vocabularies are read");
    }

    // The posted document is read inside the responder: the client disposes the request content once the
    // call returns, so reading it afterwards finds nothing.
    private static (SubmissionIntakeService Service, Func<string?> PostedFragment) ServiceCapturingFragment()
    {
        string? captured = null;
        var service = Service(request =>
        {
            if (IsFragment(request))
                captured = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Holds(request);
        });
        return (service, () => captured);
    }

    // A fragment upserts, so a wizard posts the whole submission again on every save. None of the name
    // lookups reads another's answer, and awaiting them one after another spends a round trip each on every
    // save — one more again for every archetype added later.
    [Fact]
    public async Task The_name_lookups_run_together_rather_than_one_after_another()
    {
        var counting = new object();
        var inFlight = 0;
        var mostAtOnce = 0;
        // Completes as soon as a second lookup is in flight. Awaited one after another there is never a
        // second one waiting here, so the count stays at one however long each call takes.
        var aSecondArrived = new TaskCompletionSource();
        // One wait shared by every lookup, so lookups that do run one after another fail the test
        // after one ceiling rather than one each.
        var noSecondCame = new Lazy<Task>(() => Task.Delay(Settle.Ceiling));

        var service = Service(async request =>
        {
            if (IsFragment(request)) return Json("{}");

            lock (counting)
            {
                inFlight++;
                mostAtOnce = Math.Max(mostAtOnce, inFlight);
                if (inFlight >= 2) aSecondArrived.TrySetResult();
            }
            await Task.WhenAny(aSecondArrived.Task, noSecondCame.Value);
            lock (counting) inFlight--;

            return Holds(request);
        });

        await service.SubmitAsync(Document, CancellationToken.None);

        mostAtOnce.Should().BeGreaterThan(1,
            "the lookups do not depend on one another, so they should not wait for one another");
    }

    [Fact]
    public async Task The_whole_submission_is_applied_as_one_fragment()
    {
        var applications = 0;
        var service = Service(request =>
        {
            if (IsFragment(request)) applications++;
            return Holds(request);
        });

        await service.SubmitAsync(Document, CancellationToken.None);

        applications.Should().Be(1);
    }

    [Fact]
    public async Task A_predicate_the_model_holds_is_related_through_rather_than_built_again()
    {
        var studies = Guid.NewGuid();
        var service = Service(request => IsFragment(request)
            ? Json("{}")
            : Json($$"""{"Id":"{{studies}}","Name":"studies"}"""));

        var composed = await service.SubmitAsync(Document, CancellationToken.None);

        composed.Fragment.Relationships.Should().Contain(edge => edge.Predicate == studies);
        composed.Fragment.Things.Should().NotContain(thing => thing.Id == studies,
            "a predicate the model already holds must not be built a second time beside it");
    }

    [Fact]
    public async Task A_predicate_the_model_lacks_is_minted_under_a_derived_identifier()
    {
        var service = Service(request => IsFragment(request)
            ? Json("{}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var composed = await service.SubmitAsync(Document, CancellationToken.None);

        composed.Fragment.Relationships.Should()
            .Contain(edge => edge.Predicate == StableIdentity.DerivePredicate("has"),
                "two submissions into a model that holds no `has` yet must agree on which Thing it is");
        composed.Fragment.Things.Should().Contain(thing => thing.Name == "has");
    }

    [Fact]
    public async Task A_lookup_that_failed_is_not_read_as_a_predicate_the_model_lacks()
    {
        var service = Service(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.SubmitAsync(Document, CancellationToken.None));
    }

    [Fact]
    public async Task The_model_refusal_reaches_the_caller_in_its_own_words()
    {
        var service = Service(request => IsFragment(request)
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"Property 'statedAreaHectares' on 'Willow Bend' is computed."}"""),
            }
            : Holds(request));

        var refusal = await Assert.ThrowsAsync<SubmissionError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        refusal.Message.Should().Contain("statedAreaHectares").And.Contain("Willow Bend");
    }

    [Fact]
    public async Task A_lookup_that_answers_with_no_thing_is_read_as_a_predicate_the_model_lacks()
    {
        var service = Service(request => IsFragment(request) ? Json("{}") : Json("null"));

        var composed = await service.SubmitAsync(Document, CancellationToken.None);

        composed.Fragment.Relationships.Should()
            .Contain(edge => edge.Predicate == StableIdentity.DerivePredicate("has"),
                "the model answers a name it does not hold with an empty body as readily as with a 404");
    }

    // The record this submission's arrival is kept on, under the identifier the service derives to
    // ask whether it is already there.
    private static Guid RecordIdentity() =>
        StableIdentity.Derive(WillowBend.SubmissionId, SubmissionFragmentComposer.SubmissionRole);

    private static JsonElement PostedRecord(string fragment) =>
        JsonDocument.Parse(fragment).RootElement.GetProperty("Things").EnumerateArray()
            .Single(thing => thing.GetProperty("Id").GetGuid() == RecordIdentity())
            .GetProperty("Properties");

    // Whether the model already holds the record is the whole difference between an arrival and a save, and
    // only the service can ask. A wizard saves as the planner fills the form in, and a fragment upserts, so
    // a time written on every save would record the last save rather than the arrival.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task The_arrival_time_is_written_only_when_the_model_does_not_already_hold_the_record(
        bool alreadyHeld, bool expectTheTime)
    {
        string? captured = null;
        var service = Service(request =>
        {
            if (IsFragment(request))
                captured = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (IsThingLookupById(request) && alreadyHeld)
                return Json($$"""{"Id":"{{RecordIdentity()}}","Name":"Willow Bend Submission"}""");
            return Holds(request);
        });

        await service.SubmitAsync(Document, CancellationToken.None);

        var record = PostedRecord(captured!);
        record.TryGetProperty("submissionId", out _).Should().BeTrue();
        record.TryGetProperty("submittedAt", out var stamped).Should().Be(expectTheTime);
        if (expectTheTime)
            stamped.GetProperty("value").GetDateTime().Should().Be(WillowBend.ArrivedAt);
    }

    // The study's archetype is the one the model binds to the siteStudy role, read with the vocabularies
    // rather than looked up by a name, so a model calling it anything still has its studies typed.
    [Fact]
    public async Task The_posted_study_is_the_archetype_the_site_study_role_binds()
    {
        var (service, fragment) = ServiceCapturingFragment();

        await service.SubmitAsync(Document, CancellationToken.None);

        var posted = JsonDocument.Parse(fragment()!).RootElement;
        var studyId = StableIdentity.Derive(WillowBend.SubmissionId, "study");
        var study = posted.GetProperty("Things").EnumerateArray().Single(thing => thing.GetProperty("Id").GetGuid() == studyId);
        study.GetProperty("Properties").EnumerateObject().Should().BeEmpty(
            "everything the analysis writes is declared on the archetype the study is");

        posted.GetProperty("Relationships").EnumerateArray().Should().Contain(edge =>
            edge.GetProperty("Subject").GetGuid() == studyId
            && edge.GetProperty("Target").GetGuid() == SeededModel.Id("SiteStudy"));
    }

    [Fact]
    public async Task A_model_calling_the_parcel_archetype_and_the_studies_predicate_something_else_is_still_composed_against()
    {
        var model = DeclaredModel.Seeded()
            .Without("Parcel").WithArchetype("Plot").Bound(LandIntakeRoles.Parcel, "Plot")
            .Without("studies").With("examines").Bound(LandIntakeRoles.StudiedSite, "examines");
        string? captured = null;
        var (service, _) = ServiceReading(model, request =>
        {
            if (IsFragment(request))
                captured = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Holds(request);
        });

        await service.SubmitAsync(Document, CancellationToken.None);

        var relationships = JsonDocument.Parse(captured!).RootElement.GetProperty("Relationships").EnumerateArray().ToList();
        relationships.Should().Contain(edge =>
            edge.GetProperty("Subject").GetGuid() == StableIdentity.Derive(WillowBend.SubmissionId, "parcel")
            && edge.GetProperty("Target").GetGuid() == model.Id("Plot"));
        relationships.Should().Contain(edge =>
            edge.GetProperty("Subject").GetGuid() == StableIdentity.Derive(WillowBend.SubmissionId, "study")
            && edge.GetProperty("Predicate").GetGuid() == model.Id("examines"));
    }

    // The archetypes are the model's, not this service's: it relates Things to them and never mints one,
    // so a model that was never seeded is refused instead of quietly filling with Things nothing can tell
    // apart. Not a SubmissionError: the document was well formed, and whoever sent it cannot seed a model.
    [Fact]
    public async Task A_model_that_was_never_seeded_is_refused_as_a_fault_of_the_deployment()
    {
        var (service, _) = ServiceReading(
            new DeclaredModel(),
            request => IsFragment(request) ? Json("{}") : new HttpResponseMessage(HttpStatusCode.NotFound));

        var refusal = await Assert.ThrowsAsync<ModelNotSeededError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        refusal.Message.Should().ContainAll(
            LandIntakeRoles.Site.Word, LandIntakeRoles.Parcel.Word, LandIntakeRoles.Project.Word,
            LandIntakeRoles.PersonalDetail.Word, LandIntakeRoles.ProgrammeAllocation.Word,
            LandIntakeRoles.StudiedSite.Word, LandIntakeRoles.ServiceOrder.Word);
        refusal.Message.Should().Contain("Seed the model from the analysis templates");
        refusal.Should().NotBeAssignableTo<SubmissionError>(
            "a 400 would tell the submitter to correct something they cannot reach");
    }

    [Fact]
    public async Task The_posted_fragment_names_the_things_by_the_keys_the_model_reads()
    {
        var (service, fragment) = ServiceCapturingFragment();

        await service.SubmitAsync(Document, CancellationToken.None);

        var posted = JsonDocument.Parse(fragment()!).RootElement;
        posted.GetProperty("Things").EnumerateArray().First().TryGetProperty("Id", out _).Should().BeTrue();
        var edge = posted.GetProperty("Relationships").EnumerateArray().First();
        foreach (var key in new[] { "Subject", "Predicate", "Target" })
            edge.TryGetProperty(key, out _).Should().BeTrue($"the model reads an edge's {key} under that name");
    }
}
