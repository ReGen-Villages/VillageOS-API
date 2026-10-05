namespace vos.Taproot
{
    public class ConfigCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly MyceliumClient _mycelium;
        private readonly NameResolver _resolver;
        private readonly Dictionary<string, Func<string[], Task>> _commandHandlers;

        public ConfigCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
        {
            _arg = arg ?? string.Empty;
            _writer = writer;
            _mycelium = mycelium;
            _resolver = new NameResolver(mycelium);
            _commandHandlers = new Dictionary<string, Func<string[], Task>>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = HandlePropertyModeAsync,
                ["property-mode"] = HandlePropertyModeAsync
            };
        }

        public async Task ExecuteAsync()
        {
            var tok = _arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tok.Length == 0)
            {
                ShowHelp();
                return;
            }

            var subCommand = tok[0];
            var subArgs = tok.Skip(1).ToArray();

            await ExecuteCommandAsync(subCommand, subArgs);
        }

        private async Task ExecuteCommandAsync(string command, string[] args)
        {
            try
            {
                if (_commandHandlers.TryGetValue(command, out var handler))
                    await handler(args);
                else
                    ShowHelp();
            }
            catch (Exception ex)
            {
                _writer.WriteLine($"Error: {OperatorMessage.For(ex)}");
            }
        }

        private async Task HandlePropertyModeAsync(string[] args)
        {
            if (args.Length == 0)
            {
                await ShowCurrentModeConfigAsync();
                return;
            }

            var action = args[0].ToLowerInvariant();
            var remainingArgs = args.Skip(1).ToArray();

            await ExecuteModeActionAsync(action, remainingArgs);
        }

        private async Task ShowCurrentModeConfigAsync()
        {
            var result = await _mycelium.GetDefaultPropertyModeAsync();
            _writer.WriteLine("Property Mode Configuration:");
            _writer.WriteLine($"  Default Mode:     {GetStringProperty(result, "Mode")}");
            _writer.WriteLine($"  Ring Buffer Size: {GetIntProperty(result, "RingBufferSize")}");
            _writer.WriteLine($"  Sample Rate:      {GetIntProperty(result, "SampleRate")}");
            _writer.WriteLine($"  Sample Seconds:   {GetIntProperty(result, "SampleSeconds")}");
            WriteWhatFullHistoryKeepsInMemory(result);
            _writer.WriteLine();
            _writer.WriteLine($"Available modes: {AvailableModes(result)}");
        }

        private void WriteWhatFullHistoryKeepsInMemory(System.Text.Json.JsonElement result)
        {
            _writer.WriteLine($"  Full-History Versions In Memory: {LimitOrNone(result, "FullHistoryVersionsInMemory")}");
            _writer.WriteLine($"  Full-History Seconds In Memory:  {LimitOrNone(result, "FullHistorySecondsInMemory")}");
        }

        private static string LimitOrNone(System.Text.Json.JsonElement result, string name) =>
            GetIntProperty(result, name) is > 0 and var limit ? limit.ToString() : "no limit";

        // The platform reports which modes it accepts; printing anything else would be this client's
        // guess at another component's vocabulary.
        private static string AvailableModes(System.Text.Json.JsonElement result) =>
            result.TryGetProperty("AvailableModes", out var modes)
            && modes.ValueKind == System.Text.Json.JsonValueKind.Array
                ? string.Join(", ", modes.EnumerateArray().Select(m => m.GetString()))
                : "(not reported)";

        // Anything that is not a subcommand is taken as a mode name and sent on. The platform owns
        // the list of modes and names them when it refuses one, so a copy here could only disagree.
        private async Task ExecuteModeActionAsync(string action, string[] args)
        {
            switch (action)
            {
                case "get":
                    await HandleGetPropertyModeAsync(args);
                    return;
                case "set":
                    await HandleSetPropertyModeAsync(args);
                    return;
                default:
                    var (_, sizes) = ParseModeArgs(args);
                    await SetDefaultModeInternalAsync(action, sizes);
                    return;
            }
        }

        private async Task HandleGetPropertyModeAsync(string[] args)
        {
            if (args.Length == 0)
            {
                await ShowDefaultModeAsync();
                return;
            }

            if (args.Length < 2)
            {
                _writer.WriteLine("Usage: config mode get <thing> <propertyName>");
                return;
            }

            await ShowPropertyModeAsync(args[0], args[1]);
        }

        private async Task ShowDefaultModeAsync()
        {
            var result = await _mycelium.GetDefaultPropertyModeAsync();
            _writer.WriteLine($"Default property mode: {GetStringProperty(result, "Mode")}");
        }

        private async Task ShowPropertyModeAsync(string thingNameOrId, string propertyName)
        {
            var resolveResult = await _resolver.ResolveThingAsync(thingNameOrId);
            if (!resolveResult.IsSuccess)
            {
                _writer.WriteLine($"Error: {resolveResult.ErrorMessage}");
                return;
            }

            var result = await _mycelium.GetPropertyModeAsync(resolveResult.Id, propertyName);
            WritePropertyModeDetails(propertyName, result);
        }

        private void WritePropertyModeDetails(string propertyName, System.Text.Json.JsonElement result)
        {
            _writer.WriteLine($"Property: {propertyName}");
            _writer.WriteLine($"  Mode: {GetStringProperty(result, "Mode")}");

            var capacity = GetIntProperty(result, "RingBufferCapacity");
            if (capacity > 0)
            {
                _writer.WriteLine($"  Ring Buffer Capacity: {capacity}");
                _writer.WriteLine($"  Ring Buffer Count: {GetIntProperty(result, "RingBufferCount")}");
            }

            switch (GetStringProperty(result, "Mode"))
            {
                case "SampledByObservations":
                    _writer.WriteLine($"  Sample Rate: 1 in {GetIntProperty(result, "SampleRate")}");
                    break;
                case "SampledByTime":
                    _writer.WriteLine($"  Sample Seconds: {GetIntProperty(result, "SampleSeconds")}");
                    break;
            }
        }

        private async Task HandleSetPropertyModeAsync(string[] args)
        {
            if (args.Length == 0)
            {
                ShowSetPropertyModeUsage();
                return;
            }

            var (positionalArgs, sizes) = ParseModeArgs(args);

            switch (positionalArgs.Count)
            {
                case 1:
                    await SetDefaultModeInternalAsync(positionalArgs[0], sizes);
                    break;
                case >= 3:
                    await SetSpecificPropertyModeAsync(positionalArgs[0], positionalArgs[1], positionalArgs[2], sizes);
                    break;
                default:
                    ShowSetPropertyModeUsage();
                    break;
            }
        }

        private async Task SetSpecificPropertyModeAsync(string thingNameOrId, string propertyName, string mode, ModeSizes sizes)
        {
            if (sizes.LimitsVersionsInMemory)
            {
                _writer.WriteLine("Error: how many versions a FullHistory property keeps in memory, and for how long, is set for the whole model.");
                _writer.WriteLine("Use: config mode set <ModeName> --versionsinmemory=N --secondsinmemory=N");
                return;
            }

            var resolveResult = await _resolver.ResolveThingAsync(thingNameOrId);
            if (!resolveResult.IsSuccess)
            {
                _writer.WriteLine($"Error: {resolveResult.ErrorMessage}");
                return;
            }

            var result = await _mycelium.SetPropertyModeAsync(
                resolveResult.Id, propertyName, mode, sizes.RingBufferSize, sizes.SampleRate, sizes.SampleSeconds);
            _writer.WriteLine($"Set property '{propertyName}' mode to {GetStringProperty(result, "Mode")}");
        }

        private void ShowSetPropertyModeUsage()
        {
            _writer.WriteLine("Usage:");
            _writer.WriteLine("  config mode set <ModeName>                         - Set default mode");
            _writer.WriteLine("  config mode set <thing> <property> <ModeName>      - Set mode for specific property");
            _writer.WriteLine();
            _writer.WriteLine("Optional parameters:");
            _writer.WriteLine("  --ringbuffer=N     - Ring buffer size (for RingBuffer mode)");
            _writer.WriteLine("  --samplerate=N     - Keep 1 in N readings (for SampledByObservations mode)");
            _writer.WriteLine("  --sampleseconds=N  - Keep the newest reading in each N seconds (for SampledByTime mode)");
            _writer.WriteLine();
            _writer.WriteLine("For the whole model only, with the default mode (0 = no limit):");
            _writer.WriteLine("  --versionsinmemory=N  - Most versions a FullHistory property keeps in memory besides its first");
            _writer.WriteLine("  --secondsinmemory=N   - Seconds a replaced version of a FullHistory property stays in memory");
        }

        private async Task SetDefaultModeInternalAsync(string mode, ModeSizes sizes)
        {
            var result = await _mycelium.SetDefaultPropertyModeAsync(
                mode, sizes.RingBufferSize, sizes.SampleRate, sizes.SampleSeconds, sizes.VersionsInMemory, sizes.SecondsInMemory);
            _writer.WriteLine($"Default property mode set to: {GetStringProperty(result, "Mode")}");
            _writer.WriteLine($"  Ring Buffer Size: {GetIntProperty(result, "RingBufferSize")}");
            _writer.WriteLine($"  Sample Rate: {GetIntProperty(result, "SampleRate")}");
            _writer.WriteLine($"  Sample Seconds: {GetIntProperty(result, "SampleSeconds")}");
            WriteWhatFullHistoryKeepsInMemory(result);
        }

        private sealed record ModeSizes(
            int? RingBufferSize, int? SampleRate, int? SampleSeconds, int? VersionsInMemory, int? SecondsInMemory)
        {
            public bool LimitsVersionsInMemory => VersionsInMemory is not null || SecondsInMemory is not null;
        }

        private static (List<string> positionalArgs, ModeSizes sizes) ParseModeArgs(string[] args)
        {
            int? ringBufferSize = null;
            int? sampleRate = null;
            int? sampleSeconds = null;
            int? versionsInMemory = null;
            int? secondsInMemory = null;
            var positionalArgs = new List<string>();

            foreach (var arg in args)
            {
                if (TryParseNamedArg(arg, "--ringbuffer=", out var size))
                    ringBufferSize = size;
                else if (TryParseNamedArg(arg, "--samplerate=", out var rate))
                    sampleRate = rate;
                else if (TryParseNamedArg(arg, "--sampleseconds=", out var seconds))
                    sampleSeconds = seconds;
                else if (TryParseNamedArg(arg, "--versionsinmemory=", out var versions))
                    versionsInMemory = versions;
                else if (TryParseNamedArg(arg, "--secondsinmemory=", out var held))
                    secondsInMemory = held;
                else
                    positionalArgs.Add(arg);
            }

            return (positionalArgs, new ModeSizes(ringBufferSize, sampleRate, sampleSeconds, versionsInMemory, secondsInMemory));
        }

        private static bool TryParseNamedArg(string arg, string prefix, out int value)
        {
            value = 0;
            return arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(arg.Substring(prefix.Length), out value);
        }

        private static string GetStringProperty(System.Text.Json.JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out var prop))
                return prop.GetString() ?? "";
            return "";
        }

        private static int GetIntProperty(System.Text.Json.JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out var prop))
                return prop.GetInt32();
            return 0;
        }

        private void ShowHelp()
        {
            _writer.WriteLine("Config commands:");
            _writer.WriteLine();
            _writer.WriteLine("  config mode                                      - Show current property mode config");
            _writer.WriteLine("  config mode <ModeName>                           - Set default property mode");
            _writer.WriteLine("  config mode get                                  - Get default property mode");
            _writer.WriteLine("  config mode get <thing> <property>               - Get mode for specific property");
            _writer.WriteLine("  config mode set <ModeName>                       - Set default property mode");
            _writer.WriteLine("  config mode set <thing> <property> <ModeName>    - Set mode for specific property");
            _writer.WriteLine();
            _writer.WriteLine("Run 'config mode' to see the modes this platform accepts.");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --ringbuffer=N      Ring buffer size for RingBuffer mode (default: 100)");
            _writer.WriteLine("  --samplerate=N      Keep 1 in N readings for SampledByObservations mode (default: 100)");
            _writer.WriteLine("  --sampleseconds=N   Keep the newest reading in each N seconds for SampledByTime mode (default: 60)");
        }
    }
}
