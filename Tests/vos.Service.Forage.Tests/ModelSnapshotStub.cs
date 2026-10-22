using System.Net;
using System.Text;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Forage.Tests;

// The one Mycelium read Forage makes: a scoped snapshot. Builds the payload a real subscription
// returns, so a test declares a model rather than a JSON document.
internal static class ModelSnapshotStub
{
    // Id pins the relationship's identity where a test asserts on it — a delete names the relationship it
    // removes — and is minted where no test cares.
    internal sealed record Edge(string Subject, string Predicate, string Target, Guid? Id = null);

    // A Thing the snapshot presents as a type rather than a member of one, and the flag it carries when
    // it is one a reader finds by mark, or the role whose Thing binds it when it is one a reader finds
    // through a role. Names match the ids dictionary the scenario declares. Values are raw JSON — a string
    // value arrives quoted — for a declaration the archetype carries beside its flag.
    internal sealed record Archetype(
        string Name, string? Flag = null, IReadOnlyDictionary<string, string>? Values = null, Role? BoundBy = null);

    // The `binds` predicate Thing a role reaches its archetype along, under one identifier for every scenario.
    internal static readonly Guid BindsPredicate = new("b1bd5000-0000-5000-8000-000000000001");

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request, IReadOnlyDictionary<string, Guid> ids, params Edge[] edges) =>
        RouteSubscription(request, ids, edges, siteValues: null);

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, Guid> ids,
        Edge[] edges,
        IReadOnlyDictionary<string, string>? siteValues) =>
        RouteSubscription(request, ids, edges, siteValues, archetypes: null);

    // A value on a Thing that is neither the site nor an archetype: what a run has already written onto
    // a coverage. Raw JSON, because the properties a run writes are not all numbers.
    internal sealed record Carrying(string Thing, string Property, string JsonValue);

    // siteValues land on the FIRST named Thing, which every scenario here declares as the site: the
    // values a source's address may name have to arrive on the site itself, not on any Thing.

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, Guid> ids,
        Edge[] edges,
        IReadOnlyDictionary<string, string>? siteValues,
        IReadOnlyCollection<Archetype>? archetypes) =>
        RouteSubscription(request, ids, edges, siteValues, archetypes, carrying: null);

    internal static HttpResponseMessage? RouteSubscription(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, Guid> ids,
        Edge[] edges,
        IReadOnlyDictionary<string, string>? siteValues,
        IReadOnlyCollection<Archetype>? archetypes,
        IReadOnlyCollection<Carrying>? carrying)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.Method != HttpMethod.Post || path != "/api/subscriptions")
            return null;

        var byName = (archetypes ?? []).ToDictionary(archetype => archetype.Name, StringComparer.Ordinal);
        var siteName = ids.Keys.First();
        var carried = (carrying ?? []).GroupBy(value => value.Thing, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<Carrying>)[.. group], StringComparer.Ordinal);
        var things = ids.Select(entry => Thing(
            entry.Value, entry.Key,
            entry.Key == siteName ? siteValues : null,
            byName.GetValueOrDefault(entry.Key),
            carried.GetValueOrDefault(entry.Key)));
        var relationships = edges.Select(edge =>
            Relationship(edge.Id ?? Guid.NewGuid(), ids[edge.Subject], ids[edge.Predicate], ids[edge.Target]));

        var bound = (archetypes ?? []).Where(archetype => archetype.BoundBy is not null).ToList();
        if (bound.Count > 0)
        {
            things = things
                .Append(Thing(BindsPredicate, RoleBindings.BindsPredicateName, null, null))
                .Concat(bound.Select(archetype => Thing(archetype.BoundBy!.Id, archetype.BoundBy.Word, null, null)));
            relationships = relationships.Concat(bound.Select(archetype =>
                Relationship(Guid.NewGuid(), archetype.BoundBy!.Id, BindsPredicate, ids[archetype.Name])));
        }

        var body = "{\"subscriptionId\":\"" + Guid.NewGuid() + "\",\"watermark\":0,\"snapshot\":{"
            + "\"watermark\":0,\"things\":[" + string.Join(",", things) + "],"
            + "\"relationships\":[" + string.Join(",", relationships) + "]}}";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static string Relationship(Guid id, Guid subject, Guid predicate, Guid target) =>
        "{\"id\":\"" + id + "\",\"name\":null,\"subjectId\":\"" + subject
        + "\",\"predicateId\":\"" + predicate + "\",\"targetId\":\"" + target
        + "\",\"properties\":{},\"inheritedProperties\":{},\"states\":[]}";

    private static string Thing(
        Guid id, string name, IReadOnlyDictionary<string, string>? values, Archetype? archetype,
        IReadOnlyCollection<Carrying>? carrying = null)
    {
        var declared = values?.Select(value =>
            "\"" + value.Key + "\":{\"value\":" + value.Value + ",\"typeInfo\":null,\"mode\":null}") ?? [];
        if (archetype?.Flag != null)
            declared = declared.Append(
                "\"" + archetype.Flag + "\":{\"value\":true,\"typeInfo\":null,\"mode\":null}");
        if (archetype?.Values != null)
            declared = declared.Concat(archetype.Values.Select(value =>
                "\"" + value.Key + "\":{\"value\":" + value.Value + ",\"typeInfo\":null,\"mode\":null}"));
        foreach (var value in carrying ?? [])
            declared = declared.Append(
                "\"" + value.Property + "\":{\"value\":" + value.JsonValue + ",\"typeInfo\":null,\"mode\":null}");

        return "{\"id\":\"" + id + "\",\"name\":\"" + name + "\","
            + "\"isArchetype\":" + (archetype != null ? "true" : "false") + ","
            + "\"properties\":{" + string.Join(",", declared) + "},"
            + "\"inheritedProperties\":{},\"states\":[],\"relationships\":[]}";
    }
}
