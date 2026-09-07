namespace vos.Taproot
{
    public class StopCommandHandler : CommandHandlerWithOutputOptions
    {
        public StopCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions? options = null)
            : base(arg, writer, mycelium, options)
        {
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

                    default:
                        ShowUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                _writer.WriteLine("Error: " + OperatorMessage.For(ex));
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

            if (await _mycelium.StopServiceAsync(handlerId))
            {
                _writer.WriteLine($"Stop request sent to service {display}");
            }
            else
            {
                _writer.WriteLine($"Service {display} not found");
            }
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: stop service <handlerNameOrId> [--showguids]  - Stop a running microservice");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
