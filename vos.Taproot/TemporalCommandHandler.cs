using System.Text.Json;

namespace vos.Taproot;

public class TemporalCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient? _client;
    private readonly NameResolver? _resolver;
    private readonly Dictionary<string, Func<string[], Task>> _commandHandlers;

    public TemporalCommandHandler(string arg, TextWriter writer, MyceliumClient? client = null)
    {
        _writer = writer;
        _arg = arg;
        _client = client;
        _resolver = client != null ? new NameResolver(client) : null;
        _commandHandlers = BuildCommandHandlers();
    }

    private Dictionary<string, Func<string[], Task>> BuildCommandHandlers() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["snapshot"] = HandleSnapshotAsync,
        ["history"] = HandleHistoryAsync,
        ["at"] = HandleAtAsync,
        ["mutations"] = HandleMutationsAsync
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
                ShowHelp();
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {OperatorMessage.For(ex)}");
        }
    }

    private async Task HandleSnapshotAsync(string[] args)
    {
        var timestamp = ParseOptionalTimestamp(args, 0);
        if (timestamp.HasError)
        {
            WriteTimestampError(args[0]);
            return;
        }

        var result = await _client!.GetModelAtTimeAsync(timestamp.Value);
        WriteFormattedJson(result);
    }

    private async Task HandleHistoryAsync(string[] args)
    {
        if (args.Length < 2)
        {
            _writer.WriteLine("Usage: temporal history <thing> <property-name> [startTime] [endTime]");
            return;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return;
        }

        var timeRange = ParseTimeRange(args, 2);
        var result = await _client!.GetPropertyVersionsAsync(resolveResult.Id, args[1], timeRange.Start, timeRange.End);
        WriteFormattedJson(result);
    }

    private async Task HandleAtAsync(string[] args)
    {
        if (args.Length < 2)
        {
            _writer.WriteLine("Usage: temporal at <thing> <timestamp>");
            return;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return;
        }

        var timestamp = ParseTimestampOrNow(args[1]);
        if (timestamp.HasError)
        {
            WriteTimestampError(args[1]);
            return;
        }

        var result = await _client!.GetThingAtTimeAsync(resolveResult.Id, timestamp.Value);
        if (result == null)
            _writer.WriteLine($"Thing not found: {resolveResult.Id}");
        else
            WriteFormattedJson(result.Value);
    }

    private async Task HandleMutationsAsync(string[] args)
    {
        if (args.Length == 0)
        {
            await HandleModelMutationsAsync(args);
            return;
        }

        var mutationType = args[0].ToLowerInvariant();

        var handled = mutationType switch
        {
            "model" => await HandleModelMutationsAsync(args.Skip(1).ToArray()),
            "thing" => await HandleThingMutationsAsync(args.Skip(1).ToArray()),
            "relationship" or "rel" => await HandleRelationshipMutationsAsync(args.Skip(1).ToArray()),
            _ => await HandleImplicitThingMutationsAsync(args)
        };
    }

    private async Task<bool> HandleModelMutationsAsync(string[] args)
    {
        var timeRange = ParseTimeRange(args, 0);
        var result = await _client!.GetModelMutationsAsync(timeRange.Start, timeRange.End);
        WriteFormattedJson(result);
        return true;
    }

    private async Task<bool> HandleThingMutationsAsync(string[] args)
    {
        if (args.Length < 1)
        {
            _writer.WriteLine("Usage: temporal mutations thing <thing> [startTime] [endTime]");
            return true;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return true;
        }

        var timeRange = ParseTimeRange(args, 1);
        var result = await _client!.GetThingMutationsAsync(resolveResult.Id, timeRange.Start, timeRange.End);
        WriteFormattedJson(result);
        return true;
    }

    private async Task<bool> HandleRelationshipMutationsAsync(string[] args)
    {
        if (args.Length < 1)
        {
            _writer.WriteLine("Usage: temporal mutations relationship <relationship-id> [startTime] [endTime]");
            return true;
        }

        if (!Guid.TryParse(args[0], out var relationshipId))
        {
            _writer.WriteLine($"Invalid relationship ID: {args[0]}");
            return true;
        }

        var timeRange = ParseTimeRange(args, 1);
        var result = await _client!.GetRelationshipMutationsAsync(relationshipId, timeRange.Start, timeRange.End);
        WriteFormattedJson(result);
        return true;
    }

    private async Task<bool> HandleImplicitThingMutationsAsync(string[] args)
    {
        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (resolveResult.IsSuccess)
        {
            var timeRange = ParseTimeRange(args, 1);
            var result = await _client!.GetThingMutationsAsync(resolveResult.Id, timeRange.Start, timeRange.End);
            WriteFormattedJson(result);
        }
        else
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            _writer.WriteLine();
            _writer.WriteLine("Usage: temporal mutations [model|thing <name>|relationship <id>] [startTime] [endTime]");
            _writer.WriteLine("  Omit type to show all model mutations");
        }
        return true;
    }

    private TimestampResult ParseOptionalTimestamp(string[] args, int index)
    {
        if (args.Length <= index)
            return TimestampResult.Success(null);

        return ParseTimestampOrNow(args[index]);
    }

    private TimestampResult ParseTimestampOrNow(string input)
    {
        if (input.Equals("now", StringComparison.OrdinalIgnoreCase))
            return TimestampResult.Success(null);

        if (TryParseTimestamp(input, out var parsed))
            return TimestampResult.Success(parsed);

        return TimestampResult.Error();
    }

    private TimeRange ParseTimeRange(string[] args, int startIndex)
    {
        DateTime? start = null;
        DateTime? end = null;

        if (args.Length > startIndex && TryParseTimestamp(args[startIndex], out var st))
            start = st;
        if (args.Length > startIndex + 1 && TryParseTimestamp(args[startIndex + 1], out var et))
            end = et;

        return new TimeRange(start, end);
    }

    private static bool TryParseTimestamp(string input, out DateTime result)
    {
        return DateTime.TryParse(input, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out result);
    }

    private void WriteTimestampError(string input)
    {
        _writer.WriteLine($"Invalid timestamp: {input}");
        _writer.WriteLine("Use ISO 8601 format (e.g., 2026-01-15T12:30:00Z) or 'now'");
    }

    private void WriteFormattedJson(JsonElement element) =>
        CommandParser.WriteFormattedJson(_writer, element);

    private void ShowHelp()
    {
        _writer.WriteLine("Temporal query commands:");
        _writer.WriteLine();
        _writer.WriteLine("  temporal snapshot [timestamp]");
        _writer.WriteLine("    Get a complete snapshot of the model at a point in time.");
        _writer.WriteLine("    Use 'now' or omit timestamp for current state.");
        _writer.WriteLine();
        _writer.WriteLine("  temporal history <thing> <property> [startTime] [endTime]");
        _writer.WriteLine("    Show change history for a property within a time range.");
        _writer.WriteLine();
        _writer.WriteLine("  temporal at <thing> <timestamp>");
        _writer.WriteLine("    Show the state of a thing at a specific time.");
        _writer.WriteLine();
        _writer.WriteLine("  temporal mutations [model|thing <name>|relationship <id>] [startTime] [endTime]");
        _writer.WriteLine("    Show all property mutations within a time range.");
        _writer.WriteLine("    - mutations             Show all mutations across the model");
        _writer.WriteLine("    - mutations model       Same as above");
        _writer.WriteLine("    - mutations <name>      Show mutations for a thing by name or ID");
        _writer.WriteLine("    - mutations thing <name>  Show mutations for a thing");
        _writer.WriteLine("    - mutations rel <id>    Show mutations for a relationship");
        _writer.WriteLine();
        _writer.WriteLine("Note: <thing> can be a GUID or a unique name.");
        _writer.WriteLine("Timestamps use ISO 8601 format: 2026-01-15T12:30:00Z");
    }

    private readonly struct TimestampResult
    {
        public DateTime? Value { get; }
        public bool HasError { get; }

        private TimestampResult(DateTime? value, bool hasError)
        {
            Value = value;
            HasError = hasError;
        }

        public static TimestampResult Success(DateTime? value) => new(value, false);
        public static TimestampResult Error() => new(null, true);
    }

    private readonly struct TimeRange
    {
        public DateTime? Start { get; }
        public DateTime? End { get; }

        public TimeRange(DateTime? start, DateTime? end)
        {
            Start = start;
            End = end;
        }
    }
}
