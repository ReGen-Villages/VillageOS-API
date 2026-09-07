namespace vos.Taproot
{
    public class DeleteCommandHandler : CommandHandlerWithOutputOptions
    {
        public DeleteCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions? options = null)
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
                        await DeleteThingAsync(tok);
                        break;

                    case "relationship":
                    case "relation":
                        await DeleteRelationshipAsync(tok);
                        break;

                    case "property":
                        await DeletePropertyAsync(tok);
                        break;

                    case "rel-property":
                        await DeleteRelPropertyAsync(tok);
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

        private async Task DeleteThingAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: delete thing <nameOrId> [--showguids]");
                return;
            }

            // Get name before deleting (for display)
            var thingName = tok[0];
            var result = await _resolver.ResolveThingAsync(tok[0]);
            if (!result.IsSuccess)
            {
                _writer.WriteLine($"Error: {result.ErrorMessage}");
                return;
            }
            var id = result.Id;

            // If user provided a GUID, try to get the name for display
            if (Guid.TryParse(tok[0], out _))
            {
                thingName = await _resolver.ResolveNameAsync(id);
            }

            if (await _mycelium.DeleteThingAsync(id))
            {
                var display = _options.FormatIdentifier(thingName, id);
                _writer.WriteLine($"Deleted thing {display}");
            }
            else
            {
                var display = _options.FormatIdentifier(thingName, id);
                _writer.WriteLine($"Thing {display} not found");
            }
        }

        private async Task DeleteRelationshipAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: delete relationship <id> [--showguids]");
                return;
            }

            if (!Guid.TryParse(tok[0], out var id))
            {
                _writer.WriteLine($"Error: Invalid GUID format: {tok[0]}");
                return;
            }

            if (await _mycelium.DeleteRelationshipAsync(id))
            {
                var display = _options.ShowGuids ? id.ToString() : "relationship";
                _writer.WriteLine($"Deleted {display}");
            }
            else
            {
                _writer.WriteLine($"Relationship {id} not found");
            }
        }

        private async Task DeletePropertyAsync(string[] tok)
        {
            if (tok.Length < 2)
            {
                _writer.WriteLine("Usage: delete property <thingNameOrId> <propertyName> [--showguids]");
                return;
            }

            var result = await _resolver.ResolveThingAsync(tok[0]);
            if (!result.IsSuccess)
            {
                _writer.WriteLine($"Error: {result.ErrorMessage}");
                return;
            }
            var thingId = result.Id;

            var propertyName = tok[1];

            // Resolve thing name for display
            var thingName = await _resolver.ResolveNameAsync(thingId);
            var thingDisplay = _options.FormatIdentifier(thingName, thingId);

            if (await _mycelium.DeletePropertyAsync(thingId, propertyName))
            {
                _writer.WriteLine($"Deleted property '{propertyName}' from thing {thingDisplay}");
            }
            else
            {
                _writer.WriteLine($"Property '{propertyName}' not found on thing {thingDisplay}");
            }
        }

        private async Task DeleteRelPropertyAsync(string[] tok)
        {
            if (tok.Length < 2)
            {
                _writer.WriteLine("Usage: delete rel-property <relationshipId> <propertyName>");
                return;
            }

            if (!Guid.TryParse(tok[0], out var relId))
            {
                _writer.WriteLine("Error: relationship must be specified by GUID.");
                return;
            }

            if (await _mycelium.DeleteRelationshipPropertyAsync(relId, tok[1]))
                _writer.WriteLine($"Deleted property '{tok[1]}' from relationship {relId}");
            else
                _writer.WriteLine($"Property '{tok[1]}' not found on relationship {relId}");
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: delete thing <nameOrId> [--showguids]               - Delete a thing by name or ID");
            _writer.WriteLine("       delete relationship <id> [--showguids]              - Delete a relationship by ID");
            _writer.WriteLine("       delete property <thing> <propName> [--showguids]    - Delete a property from a thing");
            _writer.WriteLine("       delete rel-property <relId> <propName>              - Delete a property from a relationship");
            _writer.WriteLine();
            _writer.WriteLine("Note: <thing> can be either a GUID or a unique name.");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
