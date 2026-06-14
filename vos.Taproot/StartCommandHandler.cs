namespace vos.Taproot
{
    public class StartCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly MyceliumClient _mycelium;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;
        private readonly Dictionary<string, Func<string[], Task>> _commandHandlers;

        public StartCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
            : this(arg, writer, mycelium, OutputOptions.Default)
        {
        }

        public StartCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions options)
        {
            var (parsedOptions, remainingArgs) = OutputOptions.ParseFromArgs(arg);
            _options = options.ShowGuids ? options : parsedOptions;
            _arg = remainingArgs;
            _writer = writer;
            _mycelium = mycelium;
            _resolver = new NameResolver(mycelium);
            _commandHandlers = new Dictionary<string, Func<string[], Task>>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = StartServiceAsync
            };
        }

        public async Task ExecuteAsync()
        {
            var tok = (_arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tok.Length == 0)
            {
                ShowUsage();
                return;
            }

            await ExecuteCommandAsync(tok[0], tok.Skip(1).ToArray());
        }

        private async Task ExecuteCommandAsync(string command, string[] args)
        {
            try
            {
                if (_commandHandlers.TryGetValue(command, out var handler))
                    await handler(args);
                else
                    ShowUsage();
            }
            catch (Exception ex)
            {
                _writer.WriteLine("Error: " + ex.Message);
            }
        }

        private async Task StartServiceAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: start service <handlerNameOrId> [--showguids]");
                return;
            }

            var resolution = await ResolveHandlerAsync(tok[0]);
            if (!resolution.HasValue)
                return;

            var (handlerId, handlerName) = resolution.Value;
            await ExecuteStartServiceAsync(handlerId, handlerName);
        }

        private async Task<(Guid Id, string Name)?> ResolveHandlerAsync(string identifier)
        {
            var result = await _resolver.ResolveThingAsync(identifier);
            if (result.IsSuccess)
                return (result.Id, await _resolver.ResolveNameAsync(result.Id));

            if (Guid.TryParse(identifier, out var handlerId))
                return (handlerId, await _resolver.ResolveNameAsync(handlerId));

            _writer.WriteLine($"Error: {result.ErrorMessage}");
            return null;
        }

        private async Task ExecuteStartServiceAsync(Guid handlerId, string handlerName)
        {
            var display = _options.FormatIdentifier(handlerName, handlerId);
            var success = await _mycelium.StartServiceAsync(handlerId);
            var message = success
                ? $"Service {display} started successfully"
                : $"Failed to start service {display} (not registered or start failed)";
            _writer.WriteLine(message);
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: start service <handlerNameOrId> [--showguids]  - Start a registered microservice");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
