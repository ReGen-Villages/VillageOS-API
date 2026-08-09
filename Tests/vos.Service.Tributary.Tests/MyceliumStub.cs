using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace vos.Service.Tributary.Tests;

// The two Mycelium calls every /handle integration test has to answer — look the endpoint Thing up
// by name, then serve its effective properties. Each router returns null when the request is for
// something else, so a test chains them with ?? and ends on its own upstream stub.
internal static class MyceliumStub
{
    internal static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    internal static HttpResponseMessage Binary(byte[] bytes, string? contentType)
    {
        var content = new ByteArrayContent(bytes);
        if (contentType != null)
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    internal static HttpResponseMessage? RouteFindThing(HttpRequestMessage request, Guid id, string name)
    {
        if (request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath == "/api/things"
            && request.RequestUri.Query.Contains($"name={name}"))
            return Json($$"""{"Id":"{{id}}","Name":"{{name}}"}""");
        return null;
    }

    internal static HttpResponseMessage? RouteEffectiveProperties(HttpRequestMessage request, Guid id, string jsonObject)
    {
        if (request.Method == HttpMethod.Get
            && request.RequestUri!.AbsolutePath == $"/api/things/{id}/properties")
            return Json(jsonObject);
        return null;
    }
}
