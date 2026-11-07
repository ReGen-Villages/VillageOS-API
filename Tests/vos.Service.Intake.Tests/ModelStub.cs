using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Tests;

// A model that answers: every name it is asked for resolves to a Thing, and a fragment applies.
// Tests that care about one call in particular recognise it with IsFragment and answer the
// rest with Holds.
public static class ModelStub
{
    public static bool IsFragment(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath == "/api/model/fragment";

    // The one-shot read the declarations are resolved through.
    public static bool IsDeclarationRead(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/snapshots";

    public static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Holds(HttpRequestMessage request)
    {
        if (IsFragment(request)) return Json("{}");
        if (IsDeclarationRead(request)) return Json(DeclaredVocabularyAnswer);
        // A model that answers every name still holds no submission it has not been sent, so a lookup by
        // identifier is the one thing this stub says no to.
        if (IsThingLookupById(request)) return NotFound();
        return Json($$"""{"Id":"{{Guid.NewGuid()}}","Name":"predicate"}""");
    }

    // What the declaration read answers with: a model declaring both vocabularies, serialised the way
    // the broker serialises it, so the tests that go through the real client exercise the real reading.
    public static string DeclaredVocabularyAnswer =>
        JsonSerializer.Serialize(
            new SnapshotReadResult(0, DeclaredModel.Seeded().Build()),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    // A lookup by identifier rather than by name — how the service asks whether a submission's
    // record is already there.
    public static bool IsThingLookupById(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
        && request.RequestUri!.AbsolutePath.StartsWith("/api/things/", StringComparison.Ordinal)
        && Guid.TryParse(request.RequestUri.AbsolutePath["/api/things/".Length..], out _);

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
}
