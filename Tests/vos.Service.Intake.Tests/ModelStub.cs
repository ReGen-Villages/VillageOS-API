using System.Net;
using System.Text;
using System.Web;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Services;

namespace vos.Service.Intake.Tests;

/// <summary>A model that answers: every name it is asked for resolves to a Thing, and a fragment applies.
/// Tests that care about one call in particular recognise it with <see cref="IsFragment"/> and answer the
/// rest with <see cref="Holds"/>.</summary>
public static class ModelStub
{
    public static bool IsFragment(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/api/model/fragment";

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Holds(HttpRequestMessage request) =>
        IsFragment(request) ? Json("{}") : Json($$"""{"Id":"{{Guid.NewGuid()}}","Name":"predicate"}""");

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
    ];

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
