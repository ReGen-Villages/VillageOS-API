using System.Text.Json;

namespace vos.Mcp.Tools.Process;

public sealed record NamedThing(Guid Id, string Name);

public sealed record ServiceCall(
    Guid Record,
    NamedThing Connection,
    NamedThing? Service,
    string StartedBy,
    bool KeptAcrossARestart,
    string? EnteredState,
    string? State,
    bool StillGoing,
    bool CanBeRetried,
    long? Attempts,
    string? LastAttemptAt,
    string? FinishedAt,
    string? LastError,
    string? LastRetryReason,
    IReadOnlyList<NamedThing> AlsoWatching);

public sealed record WaitingThing(Guid Id, string Name, NamedThing? AnsweredThrough);

public sealed record ServiceCallsOnAThing(
    NamedThing Thing, IReadOnlyList<ServiceCall> Calls, IReadOnlyList<WaitingThing> WaitingForThisThing);

public static class ServiceCalls
{
    public static readonly Guid ConnectionRole = new("d5a3c29f-67c1-55a5-b0e4-9241378bd08d");
    public static readonly Guid ServiceRole = new("768bb2e8-e738-5345-9126-c23ad1513197");
    public static readonly Guid CallRecordRole = new("dfe02bba-b36e-56ca-9dc3-58ca35c71940");
    public static readonly Guid NotifiedConnectionRole = new("8ac302e3-3de9-51db-8960-65f33396838e");
    public static readonly Guid AwaitedThingRole = new("6548fbdd-3474-5043-9864-abb665bb2d4b");

    public const string StartedByAState = "a Thing entering a state";
    public const string StartedByARelationship = "a relationship";

    private static readonly string[] StatesStillGoing = ["Pending", "Sent", "Received"];
    private static readonly string[] StatesThatCanBeRetried = ["Failed", "Blocked"];

    private sealed record Edge(Guid Id, Guid Subject, Guid Predicate, Guid Target);

    // `stamped` holds each relationship as GET api/relationships/{id} answered it, because the platform writes a
    // call's state onto the relationship without announcing it and a selection may carry an older reading.
    public static ServiceCallsOnAThing On(Guid thing, JsonElement selection, IReadOnlyDictionary<Guid, JsonElement> stamped)
    {
        var names = selection.GetProperty("things").EnumerateArray()
            .ToDictionary(each => each.GetProperty("Id").GetGuid(), each => each.GetProperty("Name").GetString() ?? "");
        var edges = selection.GetProperty("relationships").EnumerateArray()
            .Select(each => new Edge(
                each.GetProperty("Id").GetGuid(), each.GetProperty("SubjectId").GetGuid(),
                each.GetProperty("PredicateId").GetGuid(), each.GetProperty("TargetId").GetGuid()))
            .ToList();

        NamedThing Named(Guid id) => new(id, names.GetValueOrDefault(id, id.ToString()));
        HashSet<Guid> PredicatesNamed(string name) => [.. names.Where(each => each.Value == name).Select(each => each.Key)];

        var binds = PredicatesNamed("binds");
        var isA = PredicatesNamed("is");
        Guid? BoundBy(Guid role)
        {
            var bound = edges.Where(edge => edge.Subject == role && binds.Contains(edge.Predicate)).ToList();
            return bound.Count == 1 ? bound[0].Target : null;
        }

        var kindsOf = edges.Where(edge => isA.Contains(edge.Predicate)).ToLookup(edge => edge.Subject, edge => edge.Target);
        bool IsOf(Guid thingId, Guid? kind)
        {
            if (kind is null) return false;
            var seen = new HashSet<Guid>();
            var frontier = new Queue<Guid>([thingId]);
            while (frontier.TryDequeue(out var current))
                foreach (var above in kindsOf[current])
                {
                    if (above == kind) return true;
                    if (seen.Add(above)) frontier.Enqueue(above);
                }
            return false;
        }

        var connectionKind = BoundBy(ConnectionRole);
        var serviceKind = BoundBy(ServiceRole);
        var recordPredicate = BoundBy(CallRecordRole);
        var notifies = BoundBy(NotifiedConnectionRole);
        var awaits = BoundBy(AwaitedThingRole);

        Guid ConnectionAnswering(Guid target)
        {
            var seen = new HashSet<Guid>();
            var frontier = new List<Guid> { target };
            while (frontier.Count > 0)
            {
                var next = new List<Guid>();
                foreach (var id in frontier.Where(seen.Add))
                {
                    if (edges.FirstOrDefault(edge => edge.Subject == id && edge.Predicate == notifies) is { } named)
                        return named.Target;
                    next.AddRange(kindsOf[id]);
                }
                frontier = next;
            }
            return target;
        }

        NamedThing? ServiceOf(Guid connection) =>
            edges.FirstOrDefault(edge => edge.Subject == connection && IsOf(edge.Target, serviceKind)) is { } reached
                ? Named(reached.Target)
                : null;

        var calls = new List<ServiceCall>();
        foreach (var edge in edges.Where(edge => edge.Subject == thing))
        {
            var startedByAState = edge.Predicate == recordPredicate;
            var connection = startedByAState ? ConnectionAnswering(edge.Target) : edge.Predicate;
            if (!IsOf(connection, connectionKind)) continue;

            var stamps = stamped.TryGetValue(edge.Id, out var read) && read.TryGetProperty("Properties", out var held)
                ? held
                : default;
            var state = Text(stamps, "__CallState");
            calls.Add(new ServiceCall(
                edge.Id, Named(connection), ServiceOf(connection),
                startedByAState ? StartedByAState : StartedByARelationship,
                KeptAcrossARestart: startedByAState,
                EnteredState: Text(stamps, "__CallRecordState"),
                State: state,
                StillGoing: StatesStillGoing.Contains(state),
                CanBeRetried: StatesThatCanBeRetried.Contains(state),
                Attempts: Number(stamps, "__CallAttempts"),
                LastAttemptAt: Text(stamps, "__CallLastAttemptAt"),
                FinishedAt: Text(stamps, "__CallFinishedAt"),
                LastError: Text(stamps, "__CallLastError"),
                LastRetryReason: Text(stamps, "__CallRetryReason"),
                AlsoWatching: [.. (Text(stamps, "__CallWatchingConnections") ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Guid.Parse).Where(watching => watching != connection).Select(Named)]));
        }

        var waiting = edges
            .Where(edge => edge.Target == thing && edge.Predicate == awaits)
            .Select(edge =>
            {
                var answering = ConnectionAnswering(edge.Subject);
                return new WaitingThing(
                    edge.Subject, Named(edge.Subject).Name, IsOf(answering, connectionKind) ? Named(answering) : null);
            })
            .OrderBy(each => each.Name, StringComparer.Ordinal)
            .ToList();

        return new ServiceCallsOnAThing(
            Named(thing),
            [.. calls.OrderBy(call => call.LastAttemptAt is null).ThenBy(call => call.LastAttemptAt, StringComparer.Ordinal)
                .ThenBy(call => call.Connection.Name, StringComparer.Ordinal)],
            waiting);
    }

    private static JsonElement? Value(JsonElement stamps, string name)
    {
        if (stamps.ValueKind != JsonValueKind.Object || !stamps.TryGetProperty(name, out var held)) return null;
        return held.ValueKind == JsonValueKind.Object && held.TryGetProperty("value", out var enveloped) ? enveloped : held;
    }

    private static string? Text(JsonElement stamps, string name) =>
        Value(stamps, name) is { ValueKind: JsonValueKind.String } text && text.GetString() is { Length: > 0 } written
            ? written
            : null;

    private static long? Number(JsonElement stamps, string name) =>
        Value(stamps, name) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out var whole) ? whole : null;
}
