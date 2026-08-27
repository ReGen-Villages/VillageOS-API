using System.Net;
using System.Text;

namespace vos.Service.Forage.Tests;

// The one Mycelium read Forage makes: a scoped snapshot. Builds the payload a real subscription
// returns, so a test declares a model rather than a JSON document.
internal static class ModelSnapshotStub
{
    // Id pins the relationship's identity where a test asserts on it — a delete names the edge it
    // removes — and is minted where no test cares.
    internal sealed record Edge(string Subject, string Predicate, string Target, Guid? Id = null);

    // A Thing the snapshot presents as a type rather than a member of one, and the flag it carries when
    // it is one a reader finds by mark. Names match the ids dictionary the scenario declares. Values are
    // raw JSON — a string value arrives quoted — for a declaration the archetype carries beside its flag.
    internal sealed record Archetype(
        string Name, string? Flag = null, IReadOnlyDictionary<string, string>? Values = null);

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request, IReadOnlyDictionary<string, Guid> ids, params Edge[] edges) =>
        RouteSubscription(request, ids, edges, siteValues: null);

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, Guid> ids,
        Edge[] edges,
        IReadOnlyDictionary<string, string>? siteValues) =>
        RouteSubscription(request, ids, edges, siteValues, archetypes: null);

    // siteValues land on the FIRST named Thing, which every scenario here declares as the site: the
    // values a source's address may name have to arrive on the site itself, not on any Thing.
    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, Guid> ids,
        Edge[] edges,
        IReadOnlyDictionary<string, string>? siteValues,
        IReadOnlyCollection<Archetype>? archetypes)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.Method != HttpMethod.Post || path != "/api/subscriptions")
            return null;

        var byName = (archetypes ?? []).ToDictionary(archetype => archetype.Name, StringComparer.Ordinal);
        var siteName = ids.Keys.First();
        var things = ids.Select(entry => Thing(
            entry.Value, entry.Key,
            entry.Key == siteName ? siteValues : null,
            byName.GetValueOrDefault(entry.Key)));
        var relationships = edges.Select(edge =>
            "{\"id\":\"" + (edge.Id ?? Guid.NewGuid()) + "\",\"name\":null,\"subjectId\":\"" + ids[edge.Subject]
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

    private static string Thing(
        Guid id, string name, IReadOnlyDictionary<string, string>? values, Archetype? archetype)
    {
        var declared = values?.Select(value =>
            "\"" + value.Key + "\":{\"value\":" + value.Value + ",\"typeInfo\":null,\"mode\":null}") ?? [];
        if (archetype?.Flag != null)
            declared = declared.Append(
                "\"" + archetype.Flag + "\":{\"value\":true,\"typeInfo\":null,\"mode\":null}");
        if (archetype?.Values != null)
            declared = declared.Concat(archetype.Values.Select(value =>
                "\"" + value.Key + "\":{\"value\":" + value.Value + ",\"typeInfo\":null,\"mode\":null}"));

        return "{\"id\":\"" + id + "\",\"name\":\"" + name + "\","
            + "\"isArchetype\":" + (archetype != null ? "true" : "false") + ","
            + "\"properties\":{" + string.Join(",", declared) + "},"
            + "\"inheritedProperties\":{},\"states\":[],\"relationships\":[]}";
    }
}
