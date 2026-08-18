using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Services;
using vos.Tests.Shared;
using Xunit;
using static vos.Service.Intake.Tests.ModelStub;

namespace vos.Service.Intake.Tests;

public class SubmissionIntakeServiceTests
{
    private const string Document = $$"""
        {
          "submissionId": "{{WillowBend.SubmissionId}}",
          "site": { "name": "Willow Bend", "statedAreaHectares": 24.0, "population": 320 }
        }
        """;

    /// <summary>A service talking to a model seeded from the analysis templates: the archetypes answer, and
    /// the test says what happens to everything else. <see cref="ServiceOfAnUnseededModel"/> is the one
    /// that does not.</summary>
    private static SubmissionIntakeService Service(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        ServiceOfAnUnseededModel(Seeded(respond));

    private static SubmissionIntakeService ServiceOfAnUnseededModel(
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new IntakeMyceliumClient(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(respond)),
                NullLogger<IntakeMyceliumClient>.Instance,
                "http://localhost",
                "test-token"),
            new StubSubscriptions(DeclaredModel.Seeded().Build()),
            NullLogger<SubmissionIntakeService>.Instance);

    /// <summary>For a test whose answers must be able to overlap; a handler answering synchronously runs
    /// each call to completion before the next starts, whatever the caller did.</summary>
    private static SubmissionIntakeService ServiceOfAnUnseededModel(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(new IntakeMyceliumClient(
                new PerCallHttpClientFactory(MockHttpMessageHandler.AnsweringAsynchronously(respond)),
                NullLogger<IntakeMyceliumClient>.Instance,
                "http://localhost",
                "test-token"),
            new StubSubscriptions(DeclaredModel.Seeded().Build()),
            NullLogger<SubmissionIntakeService>.Instance);

    /// <summary>A service reading its vocabularies out of the model a test built, against archetypes that
    /// answer.</summary>
    private static (SubmissionIntakeService Service, StubSubscriptions Read) ServiceReading(DeclaredModel model)
    {
        var read = new StubSubscriptions(model.Build());
        var service = new SubmissionIntakeService(
            new IntakeMyceliumClient(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(Seeded(Holds))),
                NullLogger<IntakeMyceliumClient>.Instance,
                "http://localhost",
                "test-token"),
            read,
            NullLogger<SubmissionIntakeService>.Instance);
        return (service, read);
    }

    // A subscription left open per submission is a subscription per wizard save, and a wizard saves as the
    // planner types.
    [Fact]
    public async Task The_vocabulary_read_releases_its_subscription()
    {
        var (service, read) = ServiceReading(DeclaredModel.Seeded());

        await service.SubmitAsync(Document, CancellationToken.None);

        read.Released.Should().Be(1);
        read.AskedFor!.MarkedTypes.Should().Contain(
            DeclaredVocabularyReader.AllocationCategoryArchetypeFlag);
    }

    // A model seeded with the archetypes but not the vocabularies would take a submission and write the
    // words back with no edge, which is the state land allocation reads as every allocation uncategorised.
    [Fact]
    public async Task A_model_holding_the_archetypes_but_not_the_vocabularies_is_refused()
    {
        var (service, _) = ServiceReading(DeclaredModel.Seeded().Without("AllocationCategory"));

        var refusal = await Assert.ThrowsAsync<ModelNotSeededError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        refusal.Message.Should().Contain(DeclaredVocabularyReader.AllocationCategoryArchetypeFlag);
    }

    // Both gates refuse an unseeded model. Read after the archetypes rather than beside them, so which
    // refusal a planner sees is settled here rather than by which call answered first.
    [Fact]
    public async Task A_model_missing_everything_is_refused_by_naming_the_archetypes()
    {
        var read = new StubSubscriptions(DeclaredModel.Seeded().Without("AllocationCategory").Build());
        var service = new SubmissionIntakeService(
            new IntakeMyceliumClient(
                new PerCallHttpClientFactory(new MockHttpMessageHandler(request => IsFragment(request)
                    ? Json("{}")
                    : new HttpResponseMessage(HttpStatusCode.NotFound))),
                NullLogger<IntakeMyceliumClient>.Instance,
                "http://localhost",
                "test-token"),
            read,
            NullLogger<SubmissionIntakeService>.Instance);

        var refusal = await Assert.ThrowsAsync<ModelNotSeededError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        refusal.Message.Should().Contain(SubmissionFragmentComposer.SiteArchetypeName)
            .And.NotContain(DeclaredVocabularyReader.AllocationCategoryArchetypeFlag);
        read.AskedFor.Should().BeNull("the archetype gate refuses before anything is read");
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

        var service = ServiceOfAnUnseededModel(async request =>
        {
            if (IsFragment(request)) return Json("{}");

            lock (counting)
            {
                inFlight++;
                mostAtOnce = Math.Max(mostAtOnce, inFlight);
                if (inFlight >= 2) aSecondArrived.TrySetResult();
            }
            await Task.WhenAny(aSecondArrived.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            lock (counting) inFlight--;

            return SeededAnswer(request);
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
            .Contain(edge => edge.Predicate == StableIdentity.DerivePredicate("studies"),
                "two submissions into a model that holds no `studies` yet must agree on which Thing it is");
        composed.Fragment.Things.Should().Contain(thing => thing.Name == "studies");
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
            .Contain(edge => edge.Predicate == StableIdentity.DerivePredicate("studies"),
                "the model answers a name it does not hold with an empty body as readily as with a 404");
    }

    [Fact]
    public async Task The_posted_study_is_the_archetype_that_declares_what_the_analysis_writes()
    {
        var (service, fragment) = ServiceCapturingFragment();

        await service.SubmitAsync(Document, CancellationToken.None);

        var posted = JsonDocument.Parse(fragment()!).RootElement;
        var study = posted.GetProperty("Things").EnumerateArray().Single(thing =>
            thing.GetProperty("Properties").TryGetProperty(SubmissionFragmentComposer.SiteStudyFlag, out _));
        study.GetProperty("Properties").EnumerateObject().Select(property => property.Name)
            .Should().Equal([SubmissionFragmentComposer.SiteStudyFlag],
                "everything the analysis writes is declared on the archetype the study is");

        var archetype = StableArchetypeId(SubmissionFragmentComposer.SiteStudyArchetypeName);
        posted.GetProperty("Relationships").EnumerateArray().Should().Contain(edge =>
            edge.GetProperty("Subject").GetGuid() == study.GetProperty("Id").GetGuid()
            && edge.GetProperty("Target").GetGuid() == archetype);
    }

    // The archetypes are the model's, not this service's: it relates Things to them and never mints one,
    // so a model that was never seeded is refused instead of quietly filling with Things nothing can tell
    // apart. Not a SubmissionError: the document was well formed, and whoever sent it cannot seed a model.
    [Fact]
    public async Task A_model_that_was_never_seeded_is_refused_as_a_fault_of_the_deployment()
    {
        var service = ServiceOfAnUnseededModel(request => IsFragment(request)
            ? Json("{}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var refusal = await Assert.ThrowsAsync<ModelNotSeededError>(
            () => service.SubmitAsync(Document, CancellationToken.None));

        // Every missing name, not whichever lookup answered first. They run together, so naming one would
        // name a different one from run to run, and an unseeded model is missing all of them anyway.
        refusal.Message.Should().ContainAll(
            SubmissionFragmentComposer.SiteArchetypeName,
            SubmissionFragmentComposer.SiteStudyArchetypeName,
            SubmissionFragmentComposer.ParcelArchetypeName,
            SubmissionFragmentComposer.ProjectArchetypeName,
            SubmissionFragmentComposer.ContactArchetypeName,
            SubmissionFragmentComposer.ProgrammeAllocationArchetypeName);
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
