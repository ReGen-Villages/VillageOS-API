using System.Text.Json;

namespace vos.Taproot
{
    public class QueryCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly MyceliumClient _mycelium;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;

        public QueryCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
            : this(arg, writer, mycelium, OutputOptions.Default)
        {
        }

        public QueryCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions options)
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
                    case "property":
                        await QueryByPropertyAsync(tok);
                        break;

                    case "predicate":
                        await QueryByPredicateAsync(tok);
                        break;

                    case "stats":
                        await ShowStatsAsync();
                        break;

                    case "path":
                        _writer.WriteLine("Error: query path is not available in remote mode.");
                        _writer.WriteLine("This feature requires local graph traversal.");
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

        private async Task QueryByPropertyAsync(string[] tok)
        {
            if (tok.Length < 2)
            {
                _writer.WriteLine("Usage: query property <name> <value> [--showguids]");
                return;
            }

            var propertyName = tok[0];
            var propertyValue = string.Join(" ", tok.Skip(1));

            var allThings = await _mycelium.GetAllThingsAsync();

            if (allThings.ValueKind != JsonValueKind.Array)
            {
                _writer.WriteLine($"No things found with {propertyName} = {propertyValue}");
                return;
            }

            // Match against the server-resolved effective properties (own + all inherited), so inherited
            // values — including archetype defaults the instance never overrode — are searchable.
            var effective = await _mycelium.GetAllPropertiesAsync();
            var idToName = BuildIdToName(allThings);
            var matches = CollectPropertyMatches(effective, idToName, propertyName, propertyValue);

            if (matches.Count == 0)
            {
                _writer.WriteLine($"No things found with {propertyName} = {propertyValue}");
                return;
            }

            WritePropertyMatches(matches, propertyName, propertyValue);
        }

        private static Dictionary<string, string> BuildIdToName(JsonElement allThings)
        {
            var map = new Dictionary<string, string>();
            foreach (var thing in allThings.EnumerateArray())
            {
                var id = GetStringProperty(thing, "Id");
                if (id != null) map[id] = GetStringProperty(thing, "Name") ?? id;
            }
            return map;
        }

        private static List<(string Id, string Name, bool IsInherited, string? InheritedFrom)> CollectPropertyMatches(
            JsonElement effective, Dictionary<string, string> idToName, string propertyName, string propertyValue)
        {
            var matches = new List<(string Id, string Name, bool IsInherited, string? InheritedFrom)>();
            if (effective.ValueKind != JsonValueKind.Object) return matches;

            foreach (var thingEntry in effective.EnumerateObject())
            {
                var match = FindPropertyMatch(thingEntry.Value, propertyName, propertyValue);
                if (match.HasValue)
                    matches.Add((thingEntry.Name, idToName.GetValueOrDefault(thingEntry.Name, thingEntry.Name),
                        match.Value.IsInherited, match.Value.Source));
            }

            return matches;
        }

        // The effective map keys own properties by plain name and inherited ones by qualified path
        // ("Device.serialNumber"). Own wins over inherited for the same name (naming rules make that
        // the norm anyway); the source label is the key's path prefix.
        private static (bool IsInherited, string? Source)? FindPropertyMatch(
            JsonElement thingProps, string propertyName, string propertyValue)
        {
            if (thingProps.ValueKind != JsonValueKind.Object) return null;

            (bool IsInherited, string? Source)? inheritedMatch = null;
            foreach (var prop in thingProps.EnumerateObject())
            {
                var isInherited = prop.Value.TryGetProperty("IsInherited", out var ii) && ii.ValueKind == JsonValueKind.True;
                var leaf = isInherited ? LeafName(prop.Name) : prop.Name;
                if (leaf != propertyName) continue;
                if (!prop.Value.TryGetProperty("Value", out var val) || GetPropertyValueAsString(val) != propertyValue) continue;

                if (!isInherited) return (false, null);
                inheritedMatch ??= (true, SourcePrefix(prop.Name));
            }
            return inheritedMatch;
        }

        /// <summary>Leaf name of a qualified inherited key: "Device.serialNumber" → "serialNumber".</summary>
        private static string LeafName(string key)
        {
            var dot = key.LastIndexOf('.');
            return dot >= 0 ? key[(dot + 1)..] : key;
        }

        /// <summary>Source path of a qualified inherited key: "Device.serialNumber" → "Device".</summary>
        private static string SourcePrefix(string key)
        {
            var dot = key.LastIndexOf('.');
            return dot >= 0 ? key[..dot] : key;
        }

        private static string? GetPropertyValueAsString(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.GetRawText();
        }

        private static string? GetStringProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
        }

        private void WritePropertyMatches(
            List<(string Id, string Name, bool IsInherited, string? InheritedFrom)> matches,
            string propertyName, string propertyValue)
        {
            _writer.WriteLine($"Found {matches.Count} thing(s) with {propertyName} = {propertyValue}:");
            foreach (var (id, name, isInherited, inheritedFrom) in matches)
            {
                var display = _options.FormatIdentifier(name, id);
                var suffix = isInherited ? $" (inherited from {inheritedFrom})" : "";
                _writer.WriteLine($"  {display}{suffix}");
            }
        }

        private async Task QueryByPredicateAsync(string[] tok)
        {
            if (tok.Length == 0)
            {
                _writer.WriteLine("Usage: query predicate <predicateName> [--showguids]");
                return;
            }

            var predicateName = string.Join(" ", tok);
            var matches = await FindRelationshipsByPredicateAsync(predicateName);

            if (matches.Count == 0)
            {
                _writer.WriteLine($"No relationships found with predicate '{predicateName}'");
                return;
            }

            await WritePredicateMatchesAsync(matches, predicateName);
        }

        private async Task<List<JsonElement>> FindRelationshipsByPredicateAsync(string predicateName)
        {
            var relationships = await _mycelium.GetAllRelationshipsAsync();
            if (relationships.ValueKind != JsonValueKind.Array)
                return new List<JsonElement>();

            return relationships.EnumerateArray()
                .Where(rel => MatchesPredicate(rel, predicateName))
                .ToList();
        }

        private static bool MatchesPredicate(JsonElement rel, string predicateName)
        {
            var relName = rel.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
            return relName?.Equals(predicateName, StringComparison.OrdinalIgnoreCase) == true;
        }

        private async Task WritePredicateMatchesAsync(List<JsonElement> matches, string predicateName)
        {
            var nameMap = await _resolver.GetGuidToNameMapAsync();
            _writer.WriteLine($"Found {matches.Count} relationship(s) with predicate '{predicateName}':");

            foreach (var rel in matches)
                WriteRelationshipMatch(rel, predicateName, nameMap);
        }

        private void WriteRelationshipMatch(JsonElement rel, string predicateName, Dictionary<string, string> nameMap)
        {
            var id = rel.GetProperty("Id").GetString();
            var subjectId = rel.TryGetProperty("SubjectId", out var subProp) ? subProp.GetString() : "unknown";
            var targetId = rel.TryGetProperty("TargetId", out var tarProp) ? tarProp.GetString() : "unknown";

            var subjectName = ResolveNameFromMap(subjectId, nameMap);
            var targetName = ResolveNameFromMap(targetId, nameMap);

            var subjectDisplay = _options.FormatIdentifier(subjectName, subjectId ?? "unknown");
            var targetDisplay = _options.FormatIdentifier(targetName, targetId ?? "unknown");
            var relIdDisplay = _options.ShowGuids ? $"{id}: " : "";

            _writer.WriteLine($"  {relIdDisplay}{subjectDisplay} --[{predicateName}]--> {targetDisplay}");
        }

        private static string ResolveNameFromMap(string? id, Dictionary<string, string> nameMap)
        {
            if (id == null)
                return "unknown";
            return nameMap.TryGetValue(id.ToLowerInvariant(), out var name) ? name : id;
        }

        private async Task ShowStatsAsync()
        {
            var allThings = await _mycelium.GetAllThingsAsync();
            var allRelationships = await _mycelium.GetAllRelationshipsAsync();

            var thingCount = allThings.ValueKind == JsonValueKind.Array ? allThings.GetArrayLength() : 0;
            var relationshipCount = allRelationships.ValueKind == JsonValueKind.Array ? allRelationships.GetArrayLength() : 0;

            var totalProperties = 0;
            var handlerCount = 0;
            if (allThings.ValueKind == JsonValueKind.Array)
            {
                foreach (var thing in allThings.EnumerateArray())
                {
                    if (thing.TryGetProperty("Properties", out var props) && props.ValueKind == JsonValueKind.Object)
                    {
                        totalProperties += props.EnumerateObject().Count();
                        if (props.TryGetProperty("ExecutablePath", out _))
                            handlerCount++;
                    }
                }
            }

            var predicates = new HashSet<string>();
            if (allRelationships.ValueKind == JsonValueKind.Array)
            {
                foreach (var rel in allRelationships.EnumerateArray())
                {
                    if (rel.TryGetProperty("Name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (name != null)
                            predicates.Add(name);
                    }
                }
            }

            _writer.WriteLine("Model Statistics:");
            _writer.WriteLine($"  Things: {thingCount}");
            _writer.WriteLine($"  Relationships: {relationshipCount}");
            _writer.WriteLine($"  Predicates: {predicates.Count}");
            _writer.WriteLine($"  Total Properties: {totalProperties}");
            _writer.WriteLine($"  Handlers: {handlerCount}");

            // Top predicates
            if (allRelationships.ValueKind == JsonValueKind.Array && relationshipCount > 0)
            {
                var predicateCounts = new Dictionary<string, int>();
                foreach (var rel in allRelationships.EnumerateArray())
                {
                    var name = rel.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? "unknown" : "unknown";
                    if (predicateCounts.ContainsKey(name))
                        predicateCounts[name]++;
                    else
                        predicateCounts[name] = 1;
                }

                var topPredicates = predicateCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .ToList();

                if (topPredicates.Any())
                {
                    _writer.WriteLine();
                    _writer.WriteLine("Top Predicates:");
                    foreach (var kvp in topPredicates)
                    {
                        _writer.WriteLine($"  {kvp.Key}: {kvp.Value} relationship(s)");
                    }
                }
            }
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: query <command> [arguments] [--showguids]");
            _writer.WriteLine();
            _writer.WriteLine("Commands:");
            _writer.WriteLine("  property <name> <value>   - Find things with a specific property value");
            _writer.WriteLine("  predicate <name>          - Find all relationships with a specific predicate");
            _writer.WriteLine("  stats                     - Show model statistics");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
