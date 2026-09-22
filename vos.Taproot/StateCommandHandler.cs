using System.Text.Json;

namespace vos.Taproot;

public class StateCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient? _client;
    private readonly NameResolver? _resolver;
    private readonly Dictionary<string, Func<string[], Task>> _commandHandlers;

    public StateCommandHandler(string arg, TextWriter writer, MyceliumClient? client = null)
    {
        _writer = writer;
        _arg = arg;
        _client = client;
        _resolver = client != null ? new NameResolver(client) : null;
        _commandHandlers = BuildCommandHandlers();
    }

    private Dictionary<string, Func<string[], Task>> BuildCommandHandlers() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["get"] = HandleGetAsync,
        ["relationship"] = HandleRelationshipAsync,
        ["history"] = HandleHistoryAsync,
        ["occurrences"] = HandleOccurrencesAsync,
        ["query"] = HandleQueryAsync,
        ["find"] = HandleQueryAsync
    };

    public async Task ExecuteAsync()
    {
        if (_client == null || !CommandParser.TryParseSubcommand(_arg, out var subcommand, out var subArgs))
        {
            ShowHelp();
            return;
        }

        await ExecuteSubcommandAsync(subcommand, subArgs);
    }

    private async Task ExecuteSubcommandAsync(string subcommand, string[] args)
    {
        try
        {
            if (_commandHandlers.TryGetValue(subcommand, out var handler))
                await handler(args);
            else
                await HandleGetAsync(new[] { subcommand }.Concat(args).ToArray());
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {OperatorMessage.For(ex)}");
        }
    }

    private async Task HandleGetAsync(string[] args)
    {
        if (args.Length < 1)
        {
            _writer.WriteLine("Usage: state get <thing>");
            _writer.WriteLine("   or: state <thing>");
            return;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return;
        }

        var result = await _client!.GetStatesAsync(resolveResult.Id);
        WriteFormattedJson(result);
    }

    private async Task HandleRelationshipAsync(string[] args)
    {
        if (args.Length < 1 || !Guid.TryParse(args[0], out var relationshipId))
        {
            _writer.WriteLine("Usage: state relationship <id>");
            return;
        }

        WriteFormattedJson(await _client!.GetRelationshipStatesAsync(relationshipId));
    }

    private async Task HandleHistoryAsync(string[] args)
    {
        if (args.Length < 1)
        {
            _writer.WriteLine("Usage: state history <thing> [start] [end]");
            _writer.WriteLine("Each change of state the platform holds in memory for the thing: when, what was entered and left, and why.");
            return;
        }

        var resolved = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolved.IsSuccess)
        {
            _writer.WriteLine(resolved.ErrorMessage);
            return;
        }
        if (!TryParseWindow(args, 1, out var from, out var to))
            return;

        var answer = await _client!.GetStateTransitionsAsync(resolved.Id, from, to);
        WriteCoverage(answer, $"{answer.GetStringOrDefault("ThingName")}: state history");
        var transitions = Rows(answer, "Transitions");
        if (transitions.Count == 0)
        {
            _writer.WriteLine("No state changes in the window.");
            return;
        }

        foreach (var transition in transitions)
        {
            var line = $"{transition.GetStringOrDefault("At")}  entered [{Names(transition, "Entered")}]  left [{Names(transition, "Exited")}]";
            var property = transition.GetStringOrDefault("TriggeringProperty", "");
            if (property.Length > 0)
                line += $"  ({property}: {Value(transition, "OldValue")} -> {Value(transition, "NewValue")})";
            _writer.WriteLine(line);
        }
    }

    private async Task HandleOccurrencesAsync(string[] args)
    {
        if (args.Length < 2)
        {
            _writer.WriteLine("Usage: state occurrences <thing> <state-name> [start] [end]");
            _writer.WriteLine("Each spell the thing has spent in the state, as the platform holds it in memory.");
            return;
        }

        var resolved = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolved.IsSuccess)
        {
            _writer.WriteLine(resolved.ErrorMessage);
            return;
        }
        if (!TryParseWindow(args, 2, out var from, out var to))
            return;

        var answer = await _client!.GetStateOccurrencesAsync(resolved.Id, args[1], from, to);
        WriteCoverage(answer, $"{answer.GetStringOrDefault("ThingName")} in '{args[1]}'");
        var occurrences = Rows(answer, "Occurrences");
        if (occurrences.Count == 0)
        {
            _writer.WriteLine($"Never in '{args[1]}' in the window.");
            return;
        }

        foreach (var occurrence in occurrences)
        {
            var exited = occurrence.GetStringOrDefault("ExitedAt", "");
            _writer.WriteLine($"{occurrence.GetStringOrDefault("EnteredAt")} -> {(exited.Length > 0 ? exited : "still in it")}");
        }
    }

    private bool TryParseWindow(string[] args, int startIndex, out DateTime? from, out DateTime? to)
    {
        from = null;
        to = null;
        for (var index = startIndex; index < args.Length && index < startIndex + 2; index++)
        {
            if (!Timestamps.TryParse(args[index], out var parsed))
            {
                _writer.WriteLine($"Invalid timestamp: {args[index]}");
                _writer.WriteLine(Timestamps.ExpectedForm);
                return false;
            }
            if (index == startIndex) from = parsed;
            else to = parsed;
        }
        return true;
    }

    private void WriteCoverage(JsonElement answer, string heading)
    {
        if (answer.HasObjectProperty("Coverage"))
        {
            var coverage = answer.GetProperty("Coverage");
            _writer.WriteLine($"{heading}, {coverage.GetStringOrDefault("Source")} from {coverage.GetStringOrDefault("From")} to {coverage.GetStringOrDefault("To")}");
        }
        else
        {
            _writer.WriteLine(heading);
        }
    }

    private static List<JsonElement> Rows(JsonElement answer, string name) =>
        answer.TryGetProperty(name, out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().ToList()
            : [];

    private static string Names(JsonElement transition, string name) =>
        string.Join(", ", Rows(transition, name).Select(state => state.GetString()));

    private static string Value(JsonElement transition, string name) =>
        transition.TryGetProperty(name, out var value) ? value.FormatPropertyValue() : "";

    private async Task HandleQueryAsync(string[] args)
    {
        if (args.Length < 1)
        {
            _writer.WriteLine("Usage: state query <state-name> [--type=<kind>] [--also-in=a,b] [--not-in=c] [--within=<thing>]");
            _writer.WriteLine("                                [--limit=N] [--properties=a,b] [--include-archetypes] [--count]");
            _writer.WriteLine();
            _writer.WriteLine("Find the things currently in the specified state.");
            _writer.WriteLine("The kinds those things are — the archetypes — are left out unless --include-archetypes asks for them.");
            _writer.WriteLine("--count answers how many, and none of the things themselves.");
            return;
        }

        var options = args.Skip(1).ToArray();
        StateListNarrowing? narrowing = null;
        if (options.Length > 0)
        {
            Guid? within = null;
            if (CommandOptions.Value(options, "--within") is { } container)
            {
                var resolved = await _resolver!.ResolveThingAsync(container);
                if (!resolved.IsSuccess)
                {
                    _writer.WriteLine($"Error: {resolved.ErrorMessage}");
                    return;
                }
                within = resolved.Id;
            }

            narrowing = new StateListNarrowing(
                AlsoIn: CommandOptions.Value(options, "--also-in"),
                NotIn: CommandOptions.Value(options, "--not-in"),
                Type: CommandOptions.Value(options, "--type"),
                Within: within,
                IncludeArchetypes: CommandOptions.Has(options, "--include-archetypes"),
                Limit: CommandOptions.Number(options, "--limit"),
                Properties: CommandOptions.Value(options, "--properties"),
                CountOnly: CommandOptions.Has(options, "--count"));
        }

        var result = await _client!.GetThingsInStateAsync(args[0], narrowing);
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("Count", out var count) && count.ValueKind == JsonValueKind.Number)
            _writer.WriteLine($"{count.GetInt32()} thing(s) in state '{args[0]}'.");
        else
            WriteFormattedJson(result);
    }

    private void WriteFormattedJson(JsonElement element) =>
        CommandParser.WriteFormattedJson(_writer, element);

    private void ShowHelp()
    {
        _writer.WriteLine("State query commands:");
        _writer.WriteLine();
        _writer.WriteLine("  state get <thing>");
        _writer.WriteLine("  state <thing>");
        _writer.WriteLine("    Get current states for a thing (evaluates all ranges).");
        _writer.WriteLine();
        _writer.WriteLine("  state relationship <id>");
        _writer.WriteLine("    Get current states for a relationship (its own ranges).");
        _writer.WriteLine();
        _writer.WriteLine("  state history <thing> [start] [end]");
        _writer.WriteLine("    Each change of state the platform holds in memory: when, what was entered and left, and why.");
        _writer.WriteLine();
        _writer.WriteLine("  state occurrences <thing> <state-name> [start] [end]");
        _writer.WriteLine("    Each spell the thing has spent in the state.");
        _writer.WriteLine();
        _writer.WriteLine("  state query <state-name> [--type=<kind>] [--also-in=a,b] [--not-in=c] [--within=<thing>]");
        _writer.WriteLine("                           [--limit=N] [--properties=a,b] [--include-archetypes] [--count]");
        _writer.WriteLine("  state find <state-name>");
        _writer.WriteLine("    Find the things currently in the specified state, narrowed as the options say.");
        _writer.WriteLine("    The kinds those things are — the archetypes — are left out unless asked for; --count answers how many.");
        _writer.WriteLine();
        _writer.WriteLine("Note: <thing> can be a GUID or a unique name.");
    }
}
