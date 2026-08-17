using System.Net;
using System.Text;

namespace vos.Service.Confluence.Tests;

// The one Mycelium read Confluence makes: a scoped snapshot. Builds the payload a real subscription
// returns, so a test declares a model rather than a JSON document.
internal static class ModelSnapshotStub
{
    internal sealed record Edge(string Subject, string Predicate, string Target);

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request, IReadOnlyDictionary<string, Guid> ids, params Edge[] edges) =>
        RouteSubscription(request, ids, edges, siteValues: null);

    // siteValues land on the FIRST named Thing, which every scenario here declares as the site: the
    // values a source's address may name have to arrive on the site itself, not on any Thing.
    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, Guid> ids,
        Edge[] edges,
        IReadOnlyDictionary<string, string>? siteValues)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.Method != HttpMethod.Post || path != "/api/subscriptions")
            return null;

        var siteName = ids.Keys.First();
        var things = ids.Select(entry =>
            Thing(entry.Value, entry.Key, entry.Key == siteName ? siteValues : null));
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

    private static string Thing(Guid id, string name, IReadOnlyDictionary<string, string>? values)
    {
        var properties = values == null
            ? string.Empty
            : string.Join(",", values.Select(value =>
                "\"" + value.Key + "\":{\"value\":" + value.Value + ",\"typeInfo\":null,\"mode\":null}"));

        return "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"isArchetype\":false,"
            + "\"properties\":{" + properties + "},"
            + "\"inheritedProperties\":{},\"states\":[],\"relationships\":[]}";
    }
}
