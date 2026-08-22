using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Tests;

/// <summary>A model that answers: every name it is asked for resolves to a Thing, and a fragment applies.
/// Tests that care about one call in particular recognise it with <see cref="IsFragment"/> and answer the
/// rest with <see cref="Holds"/>.</summary>
public static class ModelStub
{
    public static bool IsFragment(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/api/model/fragment";

    /// <summary>The scoped read the vocabularies are resolved through. A subscription is released by a
    /// DELETE under the same path, which the name-lookup answer serves well enough.</summary>
    private static bool IsSubscriptionRead(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/subscriptions";

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Holds(HttpRequestMessage request)
    {
        if (IsFragment(request)) return Json("{}");
        if (IsSubscriptionRead(request)) return Json(DeclaredVocabularyAnswer);
        // A model that answers every name still holds no submission it has not been sent, so a lookup by
        // identifier is the one thing this stub says no to.
        if (IsThingLookupById(request)) return NotFound();
        return Json($$"""{"Id":"{{Guid.NewGuid()}}","Name":"predicate"}""");
    }

    /// <summary>What the scoped read answers with: a model declaring both vocabularies, serialised the way
    /// the broker serialises it, so the tests that go through the real client exercise the real reading.
    /// </summary>
    public static string DeclaredVocabularyAnswer =>
        JsonSerializer.Serialize(
            new SubscribeResult(Guid.NewGuid(), 0, DeclaredModel.Seeded().Build()),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static readonly string[] ArchetypeNames =
    [
        SubmissionFragmentComposer.SiteArchetypeName,
        SubmissionFragmentComposer.SiteStudyArchetypeName,
        SubmissionFragmentComposer.ParcelArchetypeName,
        SubmissionFragmentComposer.ProjectArchetypeName,
        SubmissionFragmentComposer.ContactArchetypeName,
        SubmissionFragmentComposer.ProgrammeAllocationArchetypeName,
        SubmissionFragmentComposer.HazardAssessmentArchetypeName,
        SubmissionFragmentComposer.DataSourceArchetypeName,
        SubmissionFragmentComposer.SubmissionArchetypeName,
    ];

    /// <summary>A lookup by identifier rather than by name — how the service asks whether a submission's
    /// record is already there.</summary>
    public static bool IsThingLookupById(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
        && request.RequestUri!.AbsolutePath.StartsWith("/api/things/", StringComparison.Ordinal)
        && Guid.TryParse(request.RequestUri.AbsolutePath["/api/things/".Length..], out _);

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);

    public static string NameAsked(HttpRequestMessage request) =>
        HttpUtility.ParseQueryString(request.RequestUri!.Query)["name"] ?? "";

    /// <summary>A model seeded from the analysis templates, which is the only kind a submission may enter.
    /// The archetypes answer with an identifier derived from their name, so two calls agree; everything
    /// else is left to the test, which is what it came to say something about.</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Seeded(
        Func<HttpRequestMessage, HttpResponseMessage> answerTheRest) =>
        request =>
        {
            if (IsFragment(request)) return answerTheRest(request);
            var name = NameAsked(request);
            return ArchetypeNames.Contains(name)
                ? Json($$"""{"Id":"{{StableArchetypeId(name)}}","Name":"{{name}}"}""")
                : answerTheRest(request);
        };

    /// <summary>What <see cref="Seeded"/> answers a name lookup with, for a test that has to wrap the
    /// answering itself rather than hand it over.</summary>
    public static HttpResponseMessage SeededAnswer(HttpRequestMessage request)
    {
        var name = NameAsked(request);
        return ArchetypeNames.Contains(name)
            ? Json($$"""{"Id":"{{StableArchetypeId(name)}}","Name":"{{name}}"}""")
            : Holds(request);
    }

    public static Guid StableArchetypeId(string archetypeName) =>
        StableIdentity.Derive(archetypeName, "archetype");
}
