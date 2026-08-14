using System.Globalization;
using System.Text.Json;

namespace vos.Taproot;

// Handles expected range commands for the CLI.
public class RangeCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly MyceliumClient? _client;
    private readonly NameResolver? _resolver;
    private readonly Dictionary<string, Func<string[], Task>> _commandHandlers;

    public RangeCommandHandler(string arg, TextWriter writer, MyceliumClient? client = null)
    {
        _writer = writer;
        _arg = arg;
        _client = client;
        _resolver = client != null ? new NameResolver(client) : null;
        _commandHandlers = BuildCommandHandlers();
    }

    private Dictionary<string, Func<string[], Task>> BuildCommandHandlers() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["create"] = HandleCreateAsync,
        ["add"] = HandleCreateAsync,
        ["list"] = HandleListAsync,
        ["get"] = HandleGetAsync,
        ["delete"] = HandleDeleteAsync,
        ["remove"] = HandleDeleteAsync,
        ["validate"] = HandleValidateAsync
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

    private async Task HandleCreateAsync(string[] args)
    {
        if (args.Length < 3)
        {
            ShowCreateUsage();
            return;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return;
        }

        var options = ParseCreateOptions(args);
        var result = await _client!.CreateRangeAsync(
            resolveResult.Id, options.Name, options.Criteria, options.Property, options.Bounds);
        WriteFormattedJson(result);
    }

    private static CreateRangeOptions ParseCreateOptions(string[] args)
    {
        var options = new CreateRangeOptions(args[1], args[2]);

        for (int i = 3; i < args.Length; i++)
            i = ParseCreateOption(args, i, options);

        return options;
    }

    private static int ParseCreateOption(string[] args, int index, CreateRangeOptions options)
    {
        if (index + 1 >= args.Length)
            return index;

        var arg = args[index].ToLowerInvariant();
        var nextArg = args[index + 1];

        return arg switch
        {
            "--property" => SetProperty(options, nextArg, index),
            "--bounds-min" => SetBoundsMin(options, nextArg, index),
            "--bounds-max" => SetBoundsMax(options, nextArg, index),
            _ => index
        };
    }

    private static int SetProperty(CreateRangeOptions options, string value, int index)
    {
        options.Property = value;
        return index + 1;
    }

    private static int SetBoundsMin(CreateRangeOptions options, string value, int index)
    {
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var min))
            options.BoundsMin = min;
        return index + 1;
    }

    private static int SetBoundsMax(CreateRangeOptions options, string value, int index)
    {
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
            options.BoundsMax = max;
        return index + 1;
    }

    private async Task HandleListAsync(string[] args)
    {
        if (args.Length < 1)
        {
            _writer.WriteLine("Usage: range list <thing>");
            return;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return;
        }

        var result = await _client!.GetRangesAsync(resolveResult.Id);
        WriteFormattedJson(result);
    }

    private async Task HandleGetAsync(string[] args)
    {
        if (args.Length < 2)
        {
            _writer.WriteLine("Usage: range get <thing> <range-name>");
            return;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return;
        }

        var result = await _client!.GetRangeAsync(resolveResult.Id, args[1]);
        if (result == null)
            _writer.WriteLine($"Range not found: {args[1]}");
        else
            WriteFormattedJson(result.Value);
    }

    private async Task HandleDeleteAsync(string[] args)
    {
        if (args.Length < 2)
        {
            _writer.WriteLine("Usage: range delete <thing> <range-name>");
            return;
        }

        var resolveResult = await _resolver!.ResolveThingAsync(args[0]);
        if (!resolveResult.IsSuccess)
        {
            _writer.WriteLine(resolveResult.ErrorMessage);
            return;
        }

        var success = await _client!.DeleteRangeAsync(resolveResult.Id, args[1]);
        _writer.WriteLine(success ? $"Deleted range '{args[1]}'" : $"Failed to delete range '{args[1]}'");
    }

    private async Task HandleValidateAsync(string[] args)
    {
        if (args.Length < 1)
        {
            ShowValidateUsage();
            return;
        }

        var criteria = string.Join(" ", args);
        var result = await _client!.ValidateCriteriaAsync(criteria);
        WriteValidationResult(result);
    }

    private void WriteValidationResult(JsonElement result)
    {
        if (result.TryGetProperty("isValid", out var isValid) && isValid.GetBoolean())
            _writer.WriteLine("Criteria is valid.");
        else if (result.TryGetProperty("error", out var error))
            _writer.WriteLine($"Invalid criteria: {error.GetString()}");
    }

    private void WriteFormattedJson(JsonElement element) =>
        CommandParser.WriteFormattedJson(_writer, element);

    private void ShowCreateUsage()
    {
        _writer.WriteLine("Usage: range create <thing> <name> <criteria> [options]");
        _writer.WriteLine();
        _writer.WriteLine("Options:");
        _writer.WriteLine("  --property <name>    Property this range describes");
        _writer.WriteLine("  --bounds-min <val>   Minimum numeric bound");
        _writer.WriteLine("  --bounds-max <val>   Maximum numeric bound");
        _writer.WriteLine();
        _writer.WriteLine("Examples:");
        _writer.WriteLine("  range create Sensor1 overheating \"temp > 100\"");
        _writer.WriteLine("  range create Sensor1 nominal \"temp >= 20 AND temp <= 80\" --property temp --bounds-min 20 --bounds-max 80");
    }

    private void ShowValidateUsage()
    {
        _writer.WriteLine("Usage: range validate <criteria>");
        _writer.WriteLine();
        _writer.WriteLine("Example: range validate \"temp > 100 AND rpm < 5000\"");
    }

    private void ShowHelp()
    {
        _writer.WriteLine("Expected range commands:");
        _writer.WriteLine();
        _writer.WriteLine("  range create <thing> <name> <criteria> [options]");
        _writer.WriteLine("    Create an expected range on a thing.");
        _writer.WriteLine("    Options:");
        _writer.WriteLine("      --property <name>    Property this range describes");
        _writer.WriteLine("      --bounds-min <val>   Minimum numeric bound");
        _writer.WriteLine("      --bounds-max <val>   Maximum numeric bound");
        _writer.WriteLine();
        _writer.WriteLine("  range list <thing>");
        _writer.WriteLine("    List all ranges for a thing.");
        _writer.WriteLine();
        _writer.WriteLine("  range get <thing> <name>");
        _writer.WriteLine("    Get a specific range by name.");
        _writer.WriteLine();
        _writer.WriteLine("  range delete <thing> <name>");
        _writer.WriteLine("    Delete a range from a thing.");
        _writer.WriteLine();
        _writer.WriteLine("  range validate <criteria>");
        _writer.WriteLine("    Validate criteria syntax without creating a range.");
        _writer.WriteLine();
        _writer.WriteLine("Criteria DSL Examples:");
        _writer.WriteLine("  \"temp > 100\"                     Simple comparison");
        _writer.WriteLine("  \"temp > 100 AND rpm < 5000\"      Compound expression");
        _writer.WriteLine("  \"status IN ('active', 'pending')\" Membership test");
        _writer.WriteLine("  \"[connected_to.Sensor].temp > 50\" Related thing reference");
        _writer.WriteLine();
        _writer.WriteLine("Note: <thing> can be a GUID or a unique name.");
    }

    private class CreateRangeOptions
    {
        public string Name { get; }
        public string Criteria { get; }
        public string? Property { get; set; }
        public decimal? BoundsMin { get; set; }
        public decimal? BoundsMax { get; set; }

        public CreateRangeOptions(string name, string criteria)
        {
            Name = name;
            Criteria = criteria;
        }

        public object? Bounds => BoundsMin.HasValue || BoundsMax.HasValue
            ? new { Type = "numeric", Min = BoundsMin, Max = BoundsMax }
            : null;
    }
}
