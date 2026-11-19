using System.Text.Json;
using System.Text.Json.Nodes;
using vos.Hypha.Registry;
using vos.MyceliumClient;
using vos.Pages;

namespace vos.Hypha.Tools.Pages;

/// <summary>
/// Every path and identifier a press sends comes out of a page description, which is the model's data, so each
/// is checked here before it is sent. Each write is reported in the console's words, with the caller's reason.
/// </summary>
public sealed class MyceliumPageWrites(MyceliumClient mycelium, ActivityReport report, string reason) : IPageWrites
{
    private const string TimeType = "vos.DateTime";

    private readonly List<Task<bool>> _reports = [];

    public async Task<bool> EveryReportTakenAsync() => (await Task.WhenAll(_reports)).All(taken => taken);

    /// <summary>The first write of a composed press that would not be sent, so a press is refused before any of it is.</summary>
    public static string? RefusalToSend(JsonArray writes)
    {
        foreach (var write in writes)
        {
            var refusal = (string?)write?["write"] switch
            {
                "post" => PageReadRoutes.RefusalToPress(RouteOf((string?)write!["via"] ?? "")),
                "relate" => RefusalOfIdentifiers(write!, ("subjectId", "row"), ("predicateId", "predicate"), ("targetId", "Thing the row is related to")),
                "stamp" => RefusalOfIdentifiers(write!, ("thingId", "row")),
                var other => $"A press makes no write of this kind: {JsonSerializer.Serialize(other)}",
            };
            if (refusal is not null) return refusal;
        }
        return null;
    }

    // As Tendril routes a post: a `via` written as a path is a platform route, and any other names an endpoint.
    private static string RouteOf(string via) => via.StartsWith('/') ? via : "/api/endpoints/" + via;

    private static string? RefusalOfIdentifiers(JsonNode write, params (string Field, string What)[] fields)
    {
        foreach (var (field, what) in fields)
        {
            var asked = write[field] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
            if (!Guid.TryParseExact(asked, "D", out _)) return NotAnIdentifier(what, asked);
        }
        return null;
    }

    private static string NotAnIdentifier(string what, string? asked) =>
        $"The {what} of this press is not an identifier, so nothing is sent: {JsonSerializer.Serialize(asked)}";

    public Task<string> PressAsync(string path, string bodyJson, CancellationToken cancellation) =>
        PageReadRoutes.RefusalToPress(path) is { } refusal
            ? Task.FromException<string>(new InvalidOperationException(refusal))
            : mycelium.RequestAsync(HttpMethod.Post, path, bodyJson);

    public async Task RelateAsync(string subjectId, string predicateId, string targetId, CancellationToken cancellation)
    {
        var subject = Identifier(subjectId, "row");
        var target = Identifier(targetId, "Thing the row is related to");
        var predicate = Identifier(predicateId, "predicate");
        await ReportedAsync($"relate Thing {subject} to Thing {target}", () => mycelium.CreateRelationshipAsync(subject, predicate, target));
    }

    public async Task StampAsync(string thingId, string property, string at, CancellationToken cancellation)
    {
        var thing = Identifier(thingId, "row");
        await ReportedAsync(
            $"set property {JsonSerializer.Serialize(property)} on Thing {thing}",
            () => mycelium.SetPropertyAsync(thing, property, TimeType, at));
    }

    public void RecordAction(string description, bool succeeded) => _reports.Add(report.ReportAsync(WithTheReason(description), succeeded));

    private async Task ReportedAsync(string description, Func<Task> write)
    {
        var succeeded = false;
        try
        {
            await write();
            succeeded = true;
        }
        finally
        {
            RecordAction(description, succeeded);
        }
    }

    private string WithTheReason(string description) => $"{description}, through an assistant, because: {JsonSerializer.Serialize(reason)}";

    private static Guid Identifier(string asked, string what) =>
        Guid.TryParseExact(asked, "D", out var identifier) ? identifier : throw new InvalidOperationException(NotAnIdentifier(what, asked));
}
