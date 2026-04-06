using System.Text.Json;

namespace vos.CLI;

/// <summary>
/// Handles the plant command which loads a seed file and optionally sets
/// temporal modes for all properties in the loaded model.
/// </summary>
public class PlantCommandHandler
{
    private readonly TextWriter _writer;
    private readonly string _arg;
    private readonly BrokerClient _broker;

    private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "currentonly", "ringbuffer", "sampled", "fullhistory"
    };

    public PlantCommandHandler(string arg, TextWriter writer, BrokerClient broker)
    {
        _arg = arg ?? string.Empty;
        _writer = writer;
        _broker = broker;
    }

    public async Task ExecuteAsync()
    {
        var (filePath, mode, ringBufferSize, sampleRate) = ParseArguments();

        if (string.IsNullOrEmpty(filePath))
        {
            ShowHelp();
            return;
        }

        try
        {
            await PlantSeedAsync(filePath, mode, ringBufferSize, sampleRate);
        }
        catch (Exception ex)
        {
            _writer.WriteLine($"Error: {ex.Message}");
        }
    }

    private async Task PlantSeedAsync(string filePath, string? mode, int? ringBufferSize, int? sampleRate)
    {
        // Add .json extension if not provided
        if (!Path.HasExtension(filePath))
            filePath += ".json";

        if (!File.Exists(filePath))
        {
            _writer.WriteLine($"Error: File not found: {filePath}");
            return;
        }

        // Load the seed file
        var modelJson = await File.ReadAllTextAsync(filePath);
        await _broker.SetModelAsync(modelJson);
        _writer.WriteLine($"Model loaded from {filePath}");

        // If no mode specified, we're done
        if (string.IsNullOrEmpty(mode))
            return;

        // Set temporal mode for all properties
        await SetAllPropertyModesAsync(mode, ringBufferSize, sampleRate);
    }

    private async Task SetAllPropertyModesAsync(string mode, int? ringBufferSize, int? sampleRate)
    {
        _writer.WriteLine($"Setting all properties to {mode} mode...");

        var things = await _broker.GetAllThingsAsync();
        var propertyCount = 0;
        var thingCount = 0;

        foreach (var thing in things.EnumerateArray())
        {
            if (!thing.TryGetProperty("Id", out var idProp))
                continue;

            var thingId = Guid.Parse(idProp.GetString()!);
            var thingName = GetStringProperty(thing, "Name");
            var thingPropertyCount = 0;

            // Process own properties
            if (thing.TryGetProperty("Properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                {
                    try
                    {
                        await _broker.SetPropertyModeAsync(thingId, prop.Name, mode, ringBufferSize, sampleRate);
                        propertyCount++;
                        thingPropertyCount++;
                    }
                    catch (Exception ex)
                    {
                        _writer.WriteLine($"  Warning: Could not set mode for {thingName}.{prop.Name}: {ex.Message}");
                    }
                }
            }

            if (thingPropertyCount > 0)
                thingCount++;
        }

        _writer.WriteLine($"Configured {propertyCount} properties across {thingCount} things to {mode} mode");

        if (mode.Equals("ringbuffer", StringComparison.OrdinalIgnoreCase) && ringBufferSize.HasValue)
            _writer.WriteLine($"  Ring buffer size: {ringBufferSize}");
        else if (mode.Equals("sampled", StringComparison.OrdinalIgnoreCase) && sampleRate.HasValue)
            _writer.WriteLine($"  Sample rate: 1 in {sampleRate}");
    }

    private (string? filePath, string? mode, int? ringBufferSize, int? sampleRate) ParseArguments()
    {
        var tokens = _arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? filePath = null;
        string? mode = null;
        int? ringBufferSize = null;
        int? sampleRate = null;

        foreach (var token in tokens)
        {
            if (TryParseNamedArg(token, "--ringbuffer=", out var rbSize))
            {
                ringBufferSize = rbSize;
            }
            else if (TryParseNamedArg(token, "--samplerate=", out var sr))
            {
                sampleRate = sr;
            }
            else if (filePath == null)
            {
                filePath = token;
            }
            else if (mode == null && IsValidMode(token))
            {
                mode = token;
            }
        }

        return (filePath, mode, ringBufferSize, sampleRate);
    }

    private static bool TryParseNamedArg(string arg, string prefix, out int value)
    {
        value = 0;
        return arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(arg.Substring(prefix.Length), out value);
    }

    private static bool IsValidMode(string mode) => ValidModes.Contains(mode);

    private static string GetStringProperty(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop))
            return prop.GetString() ?? "";
        return "";
    }

    private void ShowHelp()
    {
        _writer.WriteLine("Usage: plant <file> [mode] [options]");
        _writer.WriteLine();
        _writer.WriteLine("Arguments:");
        _writer.WriteLine("  <file>              Path to the seed JSON file");
        _writer.WriteLine("  [mode]              Optional temporal mode for all properties:");
        _writer.WriteLine("                        CurrentOnly  - No versioning (fastest)");
        _writer.WriteLine("                        RingBuffer   - Keep last N values");
        _writer.WriteLine("                        Sampled      - Keep every Nth change");
        _writer.WriteLine("                        FullHistory  - Complete audit trail");
        _writer.WriteLine();
        _writer.WriteLine("Options:");
        _writer.WriteLine("  --ringbuffer=N      Ring buffer size (for RingBuffer mode, default: 100)");
        _writer.WriteLine("  --samplerate=N      Sample rate (for Sampled mode, default: 100)");
        _writer.WriteLine();
        _writer.WriteLine("Examples:");
        _writer.WriteLine("  plant mymodel.json                         Load seed with default mode");
        _writer.WriteLine("  plant mymodel.json FullHistory             Load and enable full history");
        _writer.WriteLine("  plant mymodel.json RingBuffer --ringbuffer=50   Load with ring buffer of 50");
    }
}
