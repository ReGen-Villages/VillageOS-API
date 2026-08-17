using System.Net;
using System.Text;

namespace vos.Service.Confluence.Tests;

// The one Mycelium call Confluence makes: a scoped snapshot read. Builds the payload a real
// subscription returns, so a test declares a model rather than a JSON document.
internal static class ModelSnapshotStub
{
    internal sealed record Edge(string Subject, string Predicate, string Target);

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request, IReadOnlyDictionary<string, Guid> ids, params Edge[] edges)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.Method != HttpMethod.Post || path != "/api/subscriptions")
            return null;

        var things = ids.Select(entry => Thing(entry.Value, entry.Key));
        var relationships = edges.Select(edge =>
            "{\"id\":\"" + Guid.NewGuid() + "\",\"name\":null,\"subjectId\":\"" + ids[edge.Subject]
            + "\",\"predicateId\":\"" + ids[edge.Predicate] + "\",\"targetId\":\"" + ids[edge.Target]
            + "\",\"properties\":{},\"inheritedProperties\":{},\"states\":[]}");

        var body = "{\"subscriptionId\":\"" + Guid.NewGuid() + "\",\"watermark\":0,\"snapshot\":{"
            + "\"watermark\":0,\"things\":[" + string.Join(",", things) + "],"
            + "\"relationships\":[" + string.Join(",", relationships) + "]}}";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static string Thing(Guid id, string name) =>
        "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"isArchetype\":false,"
        + "\"properties\":{},\"inheritedProperties\":{},\"states\":[],\"relationships\":[]}";
}
