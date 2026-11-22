using System.Text.Json.Nodes;

namespace vos.Hypha.Tests.Pages;

internal static class HandMadePages
{
    public const string FirstPageId = "00000000-0000-4000-8000-000000000101";
    public const string SecondPageId = "00000000-0000-4000-8000-000000000102";

    private const string PageRoleId = "a18768bc-b4e7-513c-864f-842d674c1858";
    private const string PredicateRoleId = "86743302-6588-5a93-b6a5-0dde78b092c1";

    public static string Selection(params (string Id, string Name, string Description)[] pages)
    {
        JsonObject Thing(string id, string name, bool isKind = false, JsonObject? properties = null) => new()
        {
            ["Id"] = id, ["Name"] = name, ["IsArchetype"] = isKind, ["Properties"] = properties ?? new JsonObject(),
            ["DerivedProperties"] = null, ["InheritedOverrides"] = null, ["States"] = new JsonArray(), ["Relationships"] = new JsonArray(),
        };
        JsonObject Relates(int number, string subject, string predicate, string target) => new()
        {
            ["Id"] = $"00000000-0000-4000-9000-{number:D12}", ["Name"] = null, ["SubjectId"] = subject, ["PredicateId"] = predicate,
            ["TargetId"] = target, ["Properties"] = new JsonObject(), ["InheritedOverrides"] = null, ["States"] = new JsonArray(),
        };
        const string isId = "00000000-0000-4000-8000-000000000001";
        const string bindsId = "00000000-0000-4000-8000-000000000002";
        const string roleKind = "00000000-0000-4000-8000-000000000011";
        const string predicateKind = "00000000-0000-4000-8000-000000000012";
        const string pageKind = "00000000-0000-4000-8000-000000000013";

        var things = new JsonArray(
            Thing(PageRoleId, "Page"), Thing(PredicateRoleId, "Predicate"), Thing(isId, "is"), Thing(bindsId, "binds"),
            Thing(roleKind, "Role", isKind: true), Thing(predicateKind, "Predicate", isKind: true), Thing(pageKind, "Page", isKind: true));
        var relationships = new JsonArray(
            Relates(1, PageRoleId, isId, roleKind), Relates(2, PredicateRoleId, isId, roleKind),
            Relates(3, PageRoleId, bindsId, pageKind), Relates(4, PredicateRoleId, bindsId, predicateKind),
            Relates(5, isId, isId, predicateKind), Relates(6, bindsId, isId, predicateKind));
        foreach (var (id, name, description) in pages)
        {
            things.Add(Thing(id, name, properties: new JsonObject { ["specification"] = new JsonObject { ["typeInfo"] = "vos.String", ["value"] = description } }));
            relationships.Add(Relates(relationships.Count + 1, id, isId, pageKind));
        }
        return new JsonObject { ["things"] = things, ["relationships"] = relationships }.ToJsonString();
    }
}
