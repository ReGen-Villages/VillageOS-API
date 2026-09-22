using System.Text.Json;

namespace vos.Taproot
{
    public class GetCommandHandler : CommandHandlerWithOutputOptions
    {
        public GetCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions? options = null)
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
                    case "thing":
                        await GetThingAsync(tok);
                        break;

                    case "relationship":
                    case "relation":
                        await GetRelationshipAsync(tok);
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

        // A relationship has no name to resolve, so it is asked for by id alone.
        private async Task GetRelationshipAsync(string[] tok)
        {
            if (tok.Length == 0 || !Guid.TryParse(tok[0], out var id))
            {
                _writer.WriteLine("Usage: get relationship <id>");
                return;
            }

            var relationship = await _mycelium.GetRelationshipAsync(id);
            if (relationship == null)
            {
                _writer.WriteLine($"Relationship {id} not found");
                return;
            }

            CommandParser.WriteFormattedJson(_writer, relationship.Value);
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: get thing <nameOrId> [--showguids]     - Get a thing by name or ID");
            _writer.WriteLine("       get relationship <id>                  - Get a relationship by ID, with its properties");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
