namespace vos.Taproot
{
    // Handles configuration commands like property mode settings.
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
                _writer.WriteLine($"Error: {ex.Message}");
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

            if (await TryExecuteModeActionAsync(action, remainingArgs))
                return;

            HandleUnknownModeAction(action);
        }

        private async Task ShowCurrentModeConfigAsync()
        {
            var result = await _mycelium.GetDefaultPropertyModeAsync();
            _writer.WriteLine("Property Mode Configuration:");
            _writer.WriteLine($"  Default Mode:     {GetStringProperty(result, "DefaultMode")}");
            _writer.WriteLine($"  Ring Buffer Size: {GetIntProperty(result, "RingBufferSize")}");
            _writer.WriteLine($"  Sample Rate:      {GetIntProperty(result, "SampleRate")}");
            _writer.WriteLine();
            _writer.WriteLine("Available modes: CurrentOnly, RingBuffer, Sampled, FullHistory");
        }

        private async Task<bool> TryExecuteModeActionAsync(string action, string[] args)
        {
            switch (action)
            {
                case "get":
                    await HandleGetPropertyModeAsync(args);
                    return true;
                case "set":
                    await HandleSetPropertyModeAsync(args);
                    return true;
                default:
                    if (IsValidMode(action))
                    {
                        await SetDefaultModeAsync(new[] { action }.Concat(args).ToArray());
                        return true;
                    }
                    return false;
            }
        }

        private void HandleUnknownModeAction(string action)
        {
            _writer.WriteLine($"Unknown property-mode subcommand: {action}");
            _writer.WriteLine("Use: config mode [get|set] or config mode <ModeName>");
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
            _writer.WriteLine($"Default property mode: {GetStringProperty(result, "DefaultMode")}");
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
        }

        private async Task HandleSetPropertyModeAsync(string[] args)
        {
            if (args.Length == 0)
            {
                ShowSetPropertyModeUsage();
                return;
            }

            var (positionalArgs, ringBufferSize, sampleRate) = ParseModeArgs(args);

            switch (positionalArgs.Count)
            {
                case 1:
                    await SetDefaultModeInternalAsync(positionalArgs[0], ringBufferSize, sampleRate);
                    break;
                case >= 3:
                    await SetSpecificPropertyModeAsync(positionalArgs[0], positionalArgs[1], positionalArgs[2], ringBufferSize, sampleRate);
                    break;
                default:
                    ShowSetPropertyModeUsage();
                    break;
            }
        }

        private async Task SetSpecificPropertyModeAsync(string thingNameOrId, string propertyName, string mode, int? ringBufferSize, int? sampleRate)
        {
            var resolveResult = await _resolver.ResolveThingAsync(thingNameOrId);
            if (!resolveResult.IsSuccess)
            {
                _writer.WriteLine($"Error: {resolveResult.ErrorMessage}");
                return;
            }

            var result = await _mycelium.SetPropertyModeAsync(resolveResult.Id, propertyName, mode, ringBufferSize, sampleRate);
            _writer.WriteLine($"Set property '{propertyName}' mode to {GetStringProperty(result, "Mode")}");
        }

        private void ShowSetPropertyModeUsage()
        {
            _writer.WriteLine("Usage:");
            _writer.WriteLine("  config mode set <ModeName>                         - Set default mode");
            _writer.WriteLine("  config mode set <thing> <property> <ModeName>      - Set mode for specific property");
            _writer.WriteLine();
            _writer.WriteLine("Optional parameters:");
            _writer.WriteLine("  --ringbuffer=N  - Ring buffer size (for RingBuffer mode)");
            _writer.WriteLine("  --samplerate=N  - Sample rate (for Sampled mode)");
        }

        private async Task SetDefaultModeAsync(string[] args)
        {
            if (args.Length == 0)
            {
                _writer.WriteLine("Usage: config mode <ModeName> [--ringbuffer=N] [--samplerate=N]");
                return;
            }

            var (_, ringBufferSize, sampleRate) = ParseModeArgs(args.Skip(1).ToArray());
            await SetDefaultModeInternalAsync(args[0], ringBufferSize, sampleRate);
        }

        private async Task SetDefaultModeInternalAsync(string mode, int? ringBufferSize, int? sampleRate)
        {
            var result = await _mycelium.SetDefaultPropertyModeAsync(mode, ringBufferSize, sampleRate);
            _writer.WriteLine($"Default property mode set to: {GetStringProperty(result, "DefaultMode")}");
            _writer.WriteLine($"  Ring Buffer Size: {GetIntProperty(result, "RingBufferSize")}");
            _writer.WriteLine($"  Sample Rate: {GetIntProperty(result, "SampleRate")}");
        }

        private static (List<string> positionalArgs, int? ringBufferSize, int? sampleRate) ParseModeArgs(string[] args)
        {
            int? ringBufferSize = null;
            int? sampleRate = null;
            var positionalArgs = new List<string>();

            foreach (var arg in args)
            {
                if (TryParseNamedArg(arg, "--ringbuffer=", out var size))
                    ringBufferSize = size;
                else if (TryParseNamedArg(arg, "--samplerate=", out var rate))
                    sampleRate = rate;
                else
                    positionalArgs.Add(arg);
            }

            return (positionalArgs, ringBufferSize, sampleRate);
        }

        private static bool TryParseNamedArg(string arg, string prefix, out int value)
        {
            value = 0;
            return arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(arg.Substring(prefix.Length), out value);
        }

        private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "currentonly", "ringbuffer", "sampled", "fullhistory"
        };

        private static bool IsValidMode(string mode) => ValidModes.Contains(mode);

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
            _writer.WriteLine("Available modes:");
            _writer.WriteLine("  CurrentOnly   - No versioning, fastest writes (default)");
            _writer.WriteLine("  RingBuffer    - Keep last N values in circular buffer");
            _writer.WriteLine("  Sampled       - Keep every Nth change");
            _writer.WriteLine("  FullHistory   - Full temporal versioning (original behavior)");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --ringbuffer=N   Ring buffer size for RingBuffer mode (default: 100)");
            _writer.WriteLine("  --samplerate=N   Keep 1 in N changes for Sampled mode (default: 100)");
        }
    }
}
