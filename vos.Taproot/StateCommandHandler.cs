using System.Text.Json;

namespace vos.Taproot;

// Handles state query commands for the CLI.
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
        _writer.WriteLine("  state query <state-name> [--type=<kind>] [--also-in=a,b] [--not-in=c] [--within=<thing>]");
        _writer.WriteLine("                           [--limit=N] [--properties=a,b] [--include-archetypes] [--count]");
        _writer.WriteLine("  state find <state-name>");
        _writer.WriteLine("    Find the things currently in the specified state, narrowed as the options say.");
        _writer.WriteLine("    The kinds those things are — the archetypes — are left out unless asked for; --count answers how many.");
        _writer.WriteLine();
        _writer.WriteLine("Note: <thing> can be a GUID or a unique name.");
    }
}
