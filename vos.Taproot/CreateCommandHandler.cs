using System.Text.Json;

namespace vos.Taproot
{
    public class CreateCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly BrokerClient _broker;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;

        public CreateCommandHandler(string arg, TextWriter writer, BrokerClient broker)
            : this(arg, writer, broker, OutputOptions.Default)
        {
        }

        public CreateCommandHandler(string arg, TextWriter writer, BrokerClient broker, OutputOptions options)
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
                var args = tok.Skip(1).ToArray();

                switch (cmd)
                {
                    case "thing":
                        await CreateThingAsync(args);
                        break;
                    case "property":
                        await CreatePropertyAsync(args);
                        break;
                    case "relation":
                        await CreateRelationAsync(args);
                        break;
                    case "rel-property":
                        await CreateRelPropertyAsync(args);
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

        private async Task CreateThingAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: create thing <name> [--showguids]");
                return;
            }

            var result = await _broker.CreateThingAsync(tok[0]);
            var id = result.TryGetProperty("Id", out var idProp) ? idProp.GetString() : "unknown";
            var name = result.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : "unknown";

            var display = _options.FormatIdentifier(name ?? "unknown", id ?? "unknown");
            _writer.WriteLine($"Created Thing: {display}");
        }

        private async Task CreatePropertyAsync(string[] tok)
        {
            if (tok.Length < 4)
            {
                _writer.WriteLine("Usage: create property <thingNameOrId> <name> <clrType> <value> [--showguids]");
                return;
            }

            var result = await _resolver.ResolveThingAsync(tok[0]);
            if (!result.IsSuccess)
            {
                _writer.WriteLine($"Error: {result.ErrorMessage}");
                return;
            }
            var thingId = result.Id;

            var name = tok[1];
            var type = tok[2];
            var value = tok[3];

            await _broker.SetPropertyAsync(thingId, name, type, value);

            // Resolve thing name for display
            var thingName = await _resolver.ResolveNameAsync(thingId);
            var thingDisplay = _options.FormatIdentifier(thingName, thingId);
            _writer.WriteLine($"Added property '{name}' to thing {thingDisplay}");
        }

        private async Task CreateRelationAsync(string[] tok)
        {
            if (tok.Length < 3)
            {
                _writer.WriteLine("Usage: create relation <subjectNameOrId> <predicateNameOrId> <targetNameOrId> [--showguids]");
                return;
            }

            // Resolve all three references in a single batch (uses one API call)
            var results = await _resolver.ResolveThingsAsync(tok[0], tok[1], tok[2]);

            if (!results[0].IsSuccess)
            {
                _writer.WriteLine($"Error resolving subject: {results[0].ErrorMessage}");
                return;
            }

            if (!results[1].IsSuccess)
            {
                _writer.WriteLine($"Error resolving predicate: {results[1].ErrorMessage}");
                return;
            }

            if (!results[2].IsSuccess)
            {
                _writer.WriteLine($"Error resolving target: {results[2].ErrorMessage}");
                return;
            }

            var subjectId = results[0].Id;
            var predicateId = results[1].Id;
            var targetId = results[2].Id;

            var result = await _broker.CreateRelationshipAsync(subjectId, predicateId, targetId);
            var id = result.TryGetProperty("Id", out var idProp) ? idProp.GetString() : "unknown";

            // Resolve names for display
            var nameMap = await _resolver.GetGuidToNameMapAsync();
            var subjectName = nameMap.TryGetValue(subjectId.ToString().ToLowerInvariant(), out var sn) ? sn : subjectId.ToString();
            var predicateName = nameMap.TryGetValue(predicateId.ToString().ToLowerInvariant(), out var pn) ? pn : predicateId.ToString();
            var targetName = nameMap.TryGetValue(targetId.ToString().ToLowerInvariant(), out var tn) ? tn : targetId.ToString();

            var subjectDisplay = _options.FormatIdentifier(subjectName, subjectId);
            var targetDisplay = _options.FormatIdentifier(targetName, targetId);

            var relIdDisplay = _options.ShowGuids ? $" (Id: {id})" : "";
            _writer.WriteLine($"Created Relationship: {subjectDisplay} --[{predicateName}]--> {targetDisplay}{relIdDisplay}");
        }

        private async Task CreateRelPropertyAsync(string[] tok)
        {
            if (tok.Length < 4)
            {
                _writer.WriteLine("Usage: create rel-property <relationshipId> <name> <type> <value>");
                return;
            }

            if (!Guid.TryParse(tok[0], out var relId))
            {
                _writer.WriteLine("Error: relationship must be specified by GUID.");
                return;
            }

            await _broker.SetRelationshipPropertyAsync(relId, tok[1], tok[2], tok[3]);
            _writer.WriteLine($"Added property '{tok[1]}' to relationship {relId}");
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: create thing <name> [--showguids]                                   - Create a new thing");
            _writer.WriteLine("       create property <thing> <name> <type> <value> [--showguids]         - Add a property to a thing");
            _writer.WriteLine("       create rel-property <relId> <name> <type> <value>                   - Add a property to a relationship");
            _writer.WriteLine("       create relation <subject> <predicate> <target> [--showguids]        - Create a relationship");
            _writer.WriteLine();
            _writer.WriteLine("Note: <thing>, <subject>, <predicate>, <target> can be either a GUID or a unique name.");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
