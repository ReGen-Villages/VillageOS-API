namespace vos.CLI
{
    public class StopCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly BrokerClient _broker;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;

        public StopCommandHandler(string arg, TextWriter writer, BrokerClient broker)
            : this(arg, writer, broker, OutputOptions.Default)
        {
        }

        public StopCommandHandler(string arg, TextWriter writer, BrokerClient broker, OutputOptions options)
        {
            var (parsedOptions, remainingArgs) = OutputOptions.ParseFromArgs(arg);
            _options = options.ShowGuids ? options : parsedOptions;
            _arg = remainingArgs;
            _writer = writer;
            _broker = broker;
            _resolver = new NameResolver(broker);
        }

        public async Task ExecuteAsync()
        {
            try
            {
                var tok = (_arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (tok.Length == 0)
                {
                    ShowUsage();
                    return;
                }

                var cmd = tok[0];
                tok = tok.Skip(1).ToArray();

                switch (cmd)
                {
                    case "service":
                        await StopServiceAsync(tok);
                        break;

                    case "daemon":
                        await StopDaemonAsync(tok);
                        break;

                    default:
                        ShowUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                _writer.WriteLine("Error: " + ex.Message);
            }
        }

        private async Task StopServiceAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: stop service <handlerNameOrId> [--showguids]");
                return;
            }

            // Try to resolve by name first
            var result = await _resolver.ResolveThingAsync(tok[0]);
            Guid handlerId;
            string handlerName;

            if (result.IsSuccess)
            {
                handlerId = result.Id;
                handlerName = await _resolver.ResolveNameAsync(handlerId);
            }
            else if (Guid.TryParse(tok[0], out handlerId))
            {
                // Direct GUID provided
                handlerName = await _resolver.ResolveNameAsync(handlerId);
            }
            else
            {
                _writer.WriteLine($"Error: {result.ErrorMessage}");
                return;
            }

            var display = _options.FormatIdentifier(handlerName, handlerId);

            if (await _broker.StopServiceAsync(handlerId))
            {
                _writer.WriteLine($"Stop request sent to service {display}");
            }
            else
            {
                _writer.WriteLine($"Service {display} not found");
            }
        }

        private async Task StopDaemonAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: stop daemon <daemonKey>");
                _writer.WriteLine("  Use 'list daemons' to see available daemon keys.");
                return;
            }

            var daemonKey = tok[0];

            if (await _broker.StopDaemonAsync(daemonKey))
            {
                _writer.WriteLine($"Daemon '{daemonKey}' stopped");
            }
            else
            {
                _writer.WriteLine($"Daemon '{daemonKey}' not found or not running");
            }
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: stop service <handlerNameOrId> [--showguids]  - Stop a running microservice");
            _writer.WriteLine("       stop daemon <daemonKey>                       - Stop a lazy-started daemon");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
