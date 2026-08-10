using System.Net;
using System.Text;

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
}
