using System.Text.Json;

namespace vos.Taproot
{
    public class FindCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly MyceliumClient _mycelium;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;

        public FindCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
            : this(arg, writer, mycelium, OutputOptions.Default)
        {
        }

        public FindCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions options)
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
                    case "things":
                        await FindThingsAsync(tok);
                        break;

                    case "relationships":
                    case "relations":
                        await FindRelationshipsAsync(tok);
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

        private async Task FindThingsAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: find thing <pattern> [--showguids]");
                return;
            }

            var pattern = string.Join(" ", tok);
            var allThings = await _mycelium.GetAllThingsAsync();

            if (allThings.ValueKind != JsonValueKind.Array)
            {
                _writer.WriteLine("No things found.");
                return;
            }

            var matches = new List<(string Id, string Name)>();
            foreach (var thing in allThings.EnumerateArray())
            {
                var name = thing.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var id = thing.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "" : "";
                    matches.Add((id, name));
                }
            }

            if (matches.Count == 0)
            {
                _writer.WriteLine($"No things found matching '{pattern}'");
                return;
            }

            _writer.WriteLine($"Found {matches.Count} thing(s) matching '{pattern}':");
            foreach (var (id, name) in matches)
            {
                _writer.WriteLine($"  {_options.FormatIdentifier(name, id)}");
            }
        }

        private async Task FindRelationshipsAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: find relationships <thingNameOrId> [--showguids]");
                return;
            }

            var result = await _resolver.ResolveThingAsync(tok[0]);
            if (!result.IsSuccess)
            {
                _writer.WriteLine($"Error: {result.ErrorMessage}");
                return;
            }

            var thing = await _mycelium.GetThingAsync(result.Id);
            if (thing == null)
            {
                _writer.WriteLine($"Thing {result.Id} not found");
                return;
            }

            var thingName = thing.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : "Unknown";
            var thingIdStr = result.Id.ToString();
            var thingDisplay = _options.FormatIdentifier(thingName ?? "Unknown", result.Id);

            var allRelationships = await _mycelium.GetAllRelationshipsAsync();

            if (allRelationships.ValueKind != JsonValueKind.Array)
            {
                _writer.WriteLine($"No relationships found for thing '{thingDisplay}'");
                return;
            }

            var nameMap = await _resolver.GetGuidToNameMapAsync();
            var (asSubject, asTarget) = PartitionRelationships(allRelationships, thingIdStr);

            if (asSubject.Count == 0 && asTarget.Count == 0)
            {
                _writer.WriteLine($"No relationships found for thing '{thingDisplay}'");
                return;
            }

            _writer.WriteLine($"Relationships for thing '{thingDisplay}':");
            WriteRelationshipSet(asSubject, "As Subject", nameMap, thingName ?? "Unknown", thingIdStr, isSubject: true);
            WriteRelationshipSet(asTarget, "As Target", nameMap, thingName ?? "Unknown", thingIdStr, isSubject: false);
        }

        private static (List<JsonElement> AsSubject, List<JsonElement> AsTarget) PartitionRelationships(
            JsonElement allRelationships, string thingIdStr)
        {
            var asSubject = new List<JsonElement>();
            var asTarget = new List<JsonElement>();

            foreach (var rel in allRelationships.EnumerateArray())
            {
                var subjectId = rel.TryGetProperty("SubjectId", out var subProp) ? subProp.GetString() : null;
                var targetId = rel.TryGetProperty("TargetId", out var tarProp) ? tarProp.GetString() : null;

                if (subjectId == thingIdStr)
                    asSubject.Add(rel);
                if (targetId == thingIdStr)
                    asTarget.Add(rel);
            }

            return (asSubject, asTarget);
        }

        private void WriteRelationshipSet(
            List<JsonElement> relationships, string label,
            Dictionary<string, string> nameMap,
            string thingName, string thingIdStr, bool isSubject)
        {
            if (relationships.Count == 0)
                return;

            _writer.WriteLine($"  {label} ({relationships.Count}):");

            foreach (var rel in relationships)
            {
                WriteRelationship(rel, nameMap, thingName, thingIdStr, isSubject);
            }
        }

        private void WriteRelationship(
            JsonElement rel, Dictionary<string, string> nameMap,
            string thingName, string thingIdStr, bool isSubject)
        {
            var id = rel.GetProperty("Id").GetString();
            var relName = rel.TryGetProperty("Name", out var rn) ? rn.GetString() : "unknown";

            var (subjectDisplay, targetDisplay) = isSubject
                ? GetDisplaysForSubjectRole(rel, nameMap, thingName, thingIdStr)
                : GetDisplaysForTargetRole(rel, nameMap, thingName, thingIdStr);

            var relIdDisplay = _options.ShowGuids ? $"{id}: " : "";
            _writer.WriteLine($"    {relIdDisplay}{subjectDisplay} --[{relName}]--> {targetDisplay}");
        }

        private (string Subject, string Target) GetDisplaysForSubjectRole(
            JsonElement rel, Dictionary<string, string> nameMap,
            string thingName, string thingIdStr)
        {
            var targetId = rel.TryGetProperty("TargetId", out var tp) ? tp.GetString() : "unknown";
            var targetName = ResolveName(targetId, nameMap);
            return (
                _options.FormatIdentifier(thingName, thingIdStr),
                _options.FormatIdentifier(targetName, targetId ?? "unknown")
            );
        }

        private (string Subject, string Target) GetDisplaysForTargetRole(
            JsonElement rel, Dictionary<string, string> nameMap,
            string thingName, string thingIdStr)
        {
            var subjectId = rel.TryGetProperty("SubjectId", out var sp) ? sp.GetString() : "unknown";
            var subjectName = ResolveName(subjectId, nameMap);
            return (
                _options.FormatIdentifier(subjectName, subjectId ?? "unknown"),
                _options.FormatIdentifier(thingName, thingIdStr)
            );
        }

        private static string ResolveName(string? id, Dictionary<string, string> nameMap)
        {
            if (id == null)
                return "unknown";
            return nameMap.TryGetValue(id.ToLowerInvariant(), out var name) ? name : id;
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: find thing <pattern> [--showguids]           - Find things by name pattern");
            _writer.WriteLine("       find relationships <nameOrId> [--showguids]  - Find all relationships for a thing");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
