using System.Text.Json;

namespace vos.Service.Forage.Tests;

// How Forage writes an analysis or vocabulary relationship: a fragment declaring no Things, which tells it apart from
// a coverage record's fragment.
internal sealed record RelationshipWrite(
    (Guid Subject, Guid Predicate, Guid Target) Relationship,
    IReadOnlyList<(Guid Subject, Guid Predicate, Guid Target)> Replaced)
{
    public const string Route = "/api/model/fragment";

    public static RelationshipWrite? In(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri!.AbsolutePath != Route) return null;

        var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
        if (body.TryGetProperty("things", out _)) return null;

        return new RelationshipWrite(
            Ends(body.GetProperty("relationships").EnumerateArray().Single()),
            body.TryGetProperty("retractions", out var retractions) ? [.. retractions.EnumerateArray().Select(Ends)] : []);
    }

    private static (Guid, Guid, Guid) Ends(JsonElement relationship) => (
        relationship.GetProperty("subject").GetGuid(),
        relationship.GetProperty("predicate").GetGuid(),
        relationship.GetProperty("target").GetGuid());
}
