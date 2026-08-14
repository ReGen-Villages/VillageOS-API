using System.Text.Json;

namespace vos.Taproot
{
    public class GetCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly MyceliumClient _mycelium;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;

        public GetCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
            : this(arg, writer, mycelium, OutputOptions.Default)
        {
        }

        public GetCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions options)
        {
            var (parsedOptions, remainingArgs) = OutputOptions.ParseFromArgs(arg);
            _options = options.ShowGuids ? options : parsedOptions;
            _arg = remainingArgs;
            _writer = writer;
            _mycelium = mycelium;
            _resolver = new NameResolver(mycelium);
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
                    case "thing":
                        await GetThingAsync(tok);
                        break;

                    case "relationship":
                    case "relation":
                        _writer.WriteLine("Error: get relationship is not available in remote mode.");
                        _writer.WriteLine("Use 'list relations' to see all relationships.");
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

        private async Task GetThingAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: get thing <nameOrId> [--showguids]");
                return;
            }

            var result = await _resolver.ResolveThingAsync(tok[0]);
            if (!result.IsSuccess)
            {
                _writer.WriteLine($"Error: {result.ErrorMessage}");
                return;
            }
            var id = result.Id;

            var thing = await _mycelium.GetThingAsync(id);
            if (thing == null)
            {
                var thingName = await _resolver.ResolveNameAsync(id);
                var display = _options.FormatIdentifier(thingName, id);
                _writer.WriteLine($"Thing {display} not found");
                return;
            }

            var json = JsonSerializer.Serialize(thing.Value, new JsonSerializerOptions { WriteIndented = true });
            _writer.WriteLine(json);
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: get thing <nameOrId> [--showguids]     - Get a thing by name or ID");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
