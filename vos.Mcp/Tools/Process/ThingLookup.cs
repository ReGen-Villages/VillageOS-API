using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using vos.Taproot;

namespace vos.Mcp.Tools.Process;

public static class ThingLookup
{
    public static async Task<NamedThing> FindAsync(MyceliumClient mycelium, string nameOrIdentifier)
    {
        if (Guid.TryParse(nameOrIdentifier, out var identifier))
            return await mycelium.GetThingAsync(identifier) is { } byIdentifier
                ? Named(byIdentifier)
                : throw new McpException($"No Thing has the identifier {identifier}.");

        if (nameOrIdentifier.Contains(','))
            throw new McpException(
                $"A name holding a comma cannot be asked for by name. Give the identifier of '{nameOrIdentifier}'.");

        var found = await mycelium.GetThingByNameAsync(nameOrIdentifier);
        if (found.Thing is { } only) return Named(only);
        if (found.Matching.Count == 0) throw new McpException($"No Thing is named '{nameOrIdentifier}'.");

        throw new McpException(
            $"More than one Thing is named '{nameOrIdentifier}'. Give the identifier of the one meant:\n"
            + string.Join('\n', await CandidatesAsync(mycelium, found.Matching)));
    }

    private static async Task<IEnumerable<string>> CandidatesAsync(MyceliumClient mycelium, IReadOnlyList<Guid> candidates)
    {
        var selection = (await mycelium.ReadSelectionAsync(new JsonObject
        {
            ["ids"] = new JsonArray([.. candidates.Select(id => (JsonNode)id.ToString())]),
            ["names"] = new JsonArray("is"),
            ["includeIsAncestors"] = true,
        })).GetProperty("snapshot");

        var names = selection.GetProperty("things").EnumerateArray()
            .ToDictionary(thing => thing.GetProperty("Id").GetGuid(), thing => thing.GetProperty("Name").GetString());
        var isA = names.Where(named => named.Value == "is").Select(named => named.Key).ToHashSet();
        var kinds = selection.GetProperty("relationships").EnumerateArray()
            .Where(each => isA.Contains(each.GetProperty("PredicateId").GetGuid()))
            .ToLookup(each => each.GetProperty("SubjectId").GetGuid(),
                each => names.GetValueOrDefault(each.GetProperty("TargetId").GetGuid()));

        return candidates.Select(candidate =>
        {
            var kindsOfIt = kinds[candidate].OfType<string>().Order(StringComparer.Ordinal).ToList();
            return kindsOfIt.Count > 0
                ? $"{candidate} (of kind {string.Join(", ", kindsOfIt.Select(ServiceCalls.Quoted))})"
                : candidate.ToString();
        });
    }

    private static NamedThing Named(JsonElement thing) =>
        new(thing.GetProperty("Id").GetGuid(), thing.GetProperty("Name").GetString() ?? "");
}
