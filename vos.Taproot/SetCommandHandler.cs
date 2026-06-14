namespace vos.Taproot
{
    public class SetCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly MyceliumClient _mycelium;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;

        public SetCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
            : this(arg, writer, mycelium, OutputOptions.Default)
        {
        }

        public SetCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions options)
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

                if (tok.Length < 3)
                {
                    _writer.WriteLine("Usage: set <thingNameOrId> <propertyName> <value> [--showguids]");
                    _writer.WriteLine();
                    _writer.WriteLine("Options:");
                    _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
                    return;
                }

                var result = await _resolver.ResolveThingAsync(tok[0]);
                if (!result.IsSuccess)
                {
                    _writer.WriteLine($"Error: {result.ErrorMessage}");
                    return;
                }
                var id = result.Id;

                var name = tok[1];
                var value = tok[2];

                // Default to string type for simple set operations
                await _mycelium.SetPropertyAsync(id, name, "string", value);

                // Resolve thing name for display
                var thingName = await _resolver.ResolveNameAsync(id);
                var thingDisplay = _options.FormatIdentifier(thingName, id);
                _writer.WriteLine($"Set {name} = {value} on {thingDisplay}");
            }
            catch (Exception ex)
            {
                _writer.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
