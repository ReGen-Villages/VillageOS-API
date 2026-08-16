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

    // One kind an endpoint reaches, and the keys that kind requires of it.
    internal readonly record struct Kind(string Role, string Name, params string[] Requires);

    // The scoped snapshot Tributary reads to learn which kinds an endpoint reaches. The predicate
    // Things are included because a real snapshot only carries them when the selector names them —
    // leaving them out here would make every test agree with a resolver that reads nothing.
    internal static HttpResponseMessage? RouteKinds(HttpRequestMessage request, Guid endpointId, params Kind[] kinds)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.Method != HttpMethod.Post || path != "/api/subscriptions")
            return null;

        var things = new List<string> { Thing(endpointId, "endpoint") };
        var edges = new List<string>();
        foreach (var kind in kinds)
        {
            var roleId = Guid.NewGuid();
            var kindId = Guid.NewGuid();
            things.Add(Thing(roleId, kind.Role));
            things.Add(Thing(kindId, kind.Name, kind.Requires));
            edges.Add("{\"id\":\"" + Guid.NewGuid() + "\",\"name\":null,\"subjectId\":\"" + endpointId
                + "\",\"predicateId\":\"" + roleId + "\",\"targetId\":\"" + kindId
                + "\",\"properties\":{},\"inheritedProperties\":{},\"states\":[]}");
        }

        return Json("{\"subscriptionId\":\"" + Guid.NewGuid() + "\",\"watermark\":0,\"snapshot\":{"
            + "\"watermark\":0,\"things\":[" + string.Join(",", things) + "],"
            + "\"relationships\":[" + string.Join(",", edges) + "]}}");
    }

    private static string Thing(Guid id, string name, params string[] declaredProperties)
    {
        var declared = declaredProperties.Select(property =>
            "\"" + property + "\":{\"value\":null,\"typeInfo\":null,\"mode\":null}");
        return "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"isArchetype\":false,"
            + "\"properties\":{" + string.Join(",", declared) + "},"
            + "\"inheritedProperties\":{},\"states\":[],\"relationships\":[]}";
    }
}
