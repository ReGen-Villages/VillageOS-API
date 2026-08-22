using vos.Service.Shared;
using vos.Service.Tributary.Helpers;
using System.Text.RegularExpressions;
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

    // The predicate the ingest relates through to say which registration wrote onto a Thing. Fixed, so
    // a test can name the edge it expects without reading it back out of the snapshot it stubbed.
    internal static readonly Guid ObservedPredicateId = new("0bf1a7ed-0000-4000-8000-000000000001");

    internal static HttpResponseMessage? RouteRelationshipWrite(HttpRequestMessage request)
    {
        if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/relationships")
            return Json("{\"Id\":\"" + Guid.NewGuid() + "\"}");
        return null;
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
    // Derives the edges from the same effective-properties document the test already writes, the way
    // Delta derives them from a seed. A test keeps declaring its endpoint's shape in one place, and
    // the words it uses there are the ones the old string-valued keys used, so the tests read as they
    // did before while the service under test sees only edges.
    private static readonly (string Key, string Word, string Role, string Kind)[] DerivedKinds =
    [
        ("responseKind", "binary", EndpointKindRoles.ResponseBody, "BinaryResponse"),
        ("responseKind", "json", EndpointKindRoles.ResponseBody, "JsonResponse"),
        ("authKind", "tokenexchange", EndpointKindRoles.Authentication, "TokenExchangeAuth"),
        ("pagingKind", "offset", EndpointKindRoles.Paging, "OffsetPaging"),
    ];

    internal static HttpResponseMessage? RouteKindsFromProperties(
        HttpRequestMessage request, Guid endpointId, string effectiveProperties,
        IReadOnlyCollection<Guid>? alreadyObserved = null)
    {
        var kinds = new List<Kind>();
        foreach (var (key, word, role, kind) in DerivedKinds)
        {
            var declared = Regex.Match(effectiveProperties,
                "\"(?:[^\"]*\\.)?" + key + "\"\\s*:\\s*\\{\\s*\"Value\"\\s*:\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            if (declared.Success && string.Equals(declared.Groups[1].Value.Trim(), word, StringComparison.OrdinalIgnoreCase))
                kinds.Add(new Kind(role, kind));
        }
        return RouteKindsAndObserved(request, endpointId, alreadyObserved ?? [], [.. kinds]);
    }

    internal static HttpResponseMessage? RouteKinds(HttpRequestMessage request, Guid endpointId, params Kind[] kinds) =>
        RouteKindsAndObserved(request, endpointId, [], kinds);

    // The same snapshot, with the `observed` edges the endpoint already carries — what a real read
    // returns on every run after the first, and what stops a second one writing a parallel edge.
    internal static HttpResponseMessage? RouteKindsAndObserved(
        HttpRequestMessage request, Guid endpointId, IReadOnlyCollection<Guid> alreadyObserved, params Kind[] kinds)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.Method != HttpMethod.Post || path != "/api/subscriptions")
            return null;

        var things = new List<string>
        {
            Thing(endpointId, "endpoint"),
            Thing(ObservedPredicateId, ObservedEdges.PredicateName),
        };
        var edges = new List<string>();
        foreach (var kind in kinds)
        {
            var roleId = Guid.NewGuid();
            var kindId = Guid.NewGuid();
            things.Add(Thing(roleId, kind.Role));
            things.Add(Thing(kindId, kind.Name, kind.Requires));
            edges.Add(Edge(endpointId, roleId, kindId));
        }

        foreach (var observed in alreadyObserved)
            edges.Add(Edge(endpointId, ObservedPredicateId, observed));

        return Json("{\"subscriptionId\":\"" + Guid.NewGuid() + "\",\"watermark\":0,\"snapshot\":{"
            + "\"watermark\":0,\"things\":[" + string.Join(",", things) + "],"
            + "\"relationships\":[" + string.Join(",", edges) + "]}}");
    }

    private static string Edge(Guid subjectId, Guid predicateId, Guid targetId) =>
        "{\"id\":\"" + Guid.NewGuid() + "\",\"name\":null,\"subjectId\":\"" + subjectId
        + "\",\"predicateId\":\"" + predicateId + "\",\"targetId\":\"" + targetId
        + "\",\"properties\":{},\"inheritedProperties\":{},\"states\":[]}";

    private static string Thing(Guid id, string name, params string[] declaredProperties)
    {
        var declared = declaredProperties.Select(property =>
            "\"" + property + "\":{\"value\":null,\"typeInfo\":null,\"mode\":null}");
        return "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"isArchetype\":false,"
            + "\"properties\":{" + string.Join(",", declared) + "},"
            + "\"inheritedProperties\":{},\"states\":[],\"relationships\":[]}";
    }
}
