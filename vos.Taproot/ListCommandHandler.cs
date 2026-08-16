using System.Text.Json;

namespace vos.Taproot
{
    public class ListCommandHandler
    {
        private readonly TextWriter _writer;
        private readonly string _arg;
        private readonly MyceliumClient _mycelium;
        private readonly NameResolver _resolver;
        private readonly OutputOptions _options;

        public ListCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium)
            : this(arg, writer, mycelium, OutputOptions.Default)
        {
        }

        public ListCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions options)
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

                switch (cmd)
                {
                    case "things":
                        await ListThingsAsync();
                        break;

                    case "relations":
                        await ListRelationsAsync();
                        break;

                    case "predicates":
                        await ListPredicatesAsync();
                        break;

                    case "handlers":
                        await ListHandlersAsync();
                        break;

                    case "services":
                        await ListServicesAsync();
                        break;

                    case "agents":
                        await ListAgentsAsync();
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

        private async Task ListThingsAsync()
        {
            var things = await _mycelium.GetAllThingsAsync();

            if (things.ValueKind != JsonValueKind.Array || things.GetArrayLength() == 0)
            {
                _writer.WriteLine("No things found.");
                return;
            }

            // Inherited properties are resolved server-side; the per-Thing map is keyed by id and each
            // inherited entry carries a qualified path key ("Device.serialNumber") and IsInherited.
            var effective = await _mycelium.GetAllPropertiesAsync();
            var modelName = await GetModelNameAsync();
            _writer.WriteLine($"Model: {modelName}");
            _writer.WriteLine($"Things ({things.GetArrayLength()}):");

            foreach (var thing in things.EnumerateArray())
            {
                WriteThingEntry(thing, effective);
            }
        }

        private void WriteThingEntry(JsonElement thing, JsonElement effective)
        {
            var id = thing.GetStringOrDefault("Id");
            var name = thing.GetStringOrDefault("Name");
            _writer.WriteLine($"  {_options.FormatIdentifier(name, id)}");

            WriteProperties(thing, "    ");

            if (effective.ValueKind != JsonValueKind.Object || id == null ||
                !effective.TryGetProperty(id, out var props) || props.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in props.EnumerateObject())
            {
                if (!(prop.Value.TryGetProperty("IsInherited", out var ii) && ii.ValueKind == JsonValueKind.True))
                    continue;
                if (!prop.Value.TryGetProperty("Value", out var val)) continue;
                // The key is already the qualified path (source chain + leaf), matching the prior format.
                _writer.WriteLine($"    {prop.Name}: {val.FormatPropertyValue()} (inherited)");
            }
        }

        private async Task<string> GetModelNameAsync()
        {
            try
            {
                var modelJson = await _mycelium.GetModelJsonAsync();
                var model = JsonDocument.Parse(modelJson).RootElement;
                if (model.TryGetProperty("Name", out var nameProp))
                {
                    return nameProp.GetString() ?? "Unknown";
                }
            }
            catch
            {
                // Ignore errors fetching model
            }
            return "Unknown";
        }


        private async Task ListRelationsAsync()
        {
            var relationships = await _mycelium.GetAllRelationshipsAsync();

            if (relationships.ValueKind != JsonValueKind.Array || relationships.GetArrayLength() == 0)
            {
                _writer.WriteLine("No relationships found.");
                return;
            }

            var nameMap = await _resolver.GetGuidToNameMapAsync();
            var modelName = await GetModelNameAsync();

            _writer.WriteLine($"Model: {modelName}");
            _writer.WriteLine($"Relationships ({relationships.GetArrayLength()}):");

            foreach (var rel in relationships.EnumerateArray())
            {
                WriteRelationshipEntry(rel, nameMap);
            }
        }

        private void WriteRelationshipEntry(JsonElement rel, Dictionary<string, string> nameMap)
        {
            var id = rel.GetStringOrDefault("Id");
            var subjectId = rel.GetStringOrDefault("SubjectId");
            var targetId = rel.GetStringOrDefault("TargetId");
            var predicateId = rel.GetStringOrDefault("PredicateId");

            var subjectName = ResolveName(subjectId, nameMap);
            var targetName = ResolveName(targetId, nameMap);
            var predicateName = ResolveName(predicateId, nameMap);

            // The platform sends a name only for an edge given one of its own; every other edge is named
            // by the three endpoints, which the payload carries as identifiers.
            var ownName = rel.GetStringOrDefault("Name", "");
            var name = ownName.Length > 0 ? ownName : $"{subjectName} {predicateName} {targetName}";

            var relDisplay = _options.ShowGuids ? $"{name} ({id})" : name;
            var subjectDisplay = _options.FormatIdentifier(subjectName, subjectId);
            var targetDisplay = _options.FormatIdentifier(targetName, targetId);
            var predicateDisplay = predicateName != "N/A" ? _options.FormatIdentifier(predicateName, predicateId) : "N/A";

            _writer.WriteLine($"  {relDisplay}");
            _writer.WriteLine($"    Subject: {subjectDisplay} -> Target: {targetDisplay}");
            _writer.WriteLine($"    Predicate: {predicateDisplay}");

            WriteProperties(rel, "    ");
        }

        private static string ResolveName(string id, Dictionary<string, string> nameMap)
        {
            if (id == "N/A") return id;
            return nameMap.TryGetValue(id.ToLowerInvariant(), out var name) ? name : id;
        }

        private void WriteProperties(JsonElement element, string indent)
        {
            if (!element.HasObjectProperty("Properties")) return;

            var props = element.GetProperty("Properties");
            foreach (var prop in props.EnumerateObject())
            {
                _writer.WriteLine($"{indent}{prop.Name}: {prop.Value.FormatPropertyValue()}");
            }
        }

        private async Task ListPredicatesAsync()
        {
            var relationships = await _mycelium.GetAllRelationshipsAsync();

            if (relationships.ValueKind != JsonValueKind.Array || relationships.GetArrayLength() == 0)
            {
                _writer.WriteLine("No predicates found (no relationships exist).");
                return;
            }

            var predicateCounts = new Dictionary<string, int>();
            foreach (var rel in relationships.EnumerateArray())
            {
                var name = rel.GetStringOrDefault("Name", "unknown");
                predicateCounts[name] = predicateCounts.GetValueOrDefault(name) + 1;
            }

            _writer.WriteLine($"Predicates ({predicateCounts.Count}):");
            foreach (var kvp in predicateCounts.OrderBy(k => k.Key))
            {
                _writer.WriteLine($"  {kvp.Key} (used in {kvp.Value} relationship(s))");
            }
        }

        // The platform says which connections bind a service and what each resolves to. Working that out
        // here — from a payload of Things with no relationships in it — could not read a value the
        // platform resolves by walking an edge, and would go wrong again the next time one moved.
        private async Task ListHandlersAsync()
        {
            var connections = await _mycelium.GetAllConnectionsAsync();

            if (connections.ValueKind != JsonValueKind.Array)
            {
                _writer.WriteLine("No handlers found.");
                return;
            }

            var handlers = connections.EnumerateArray()
                .Where(c => c.GetBoolOrDefault("BindsService"))
                .ToList();

            if (handlers.Count == 0)
            {
                _writer.WriteLine("No handlers found. A handler is a connection bound to a service.");
                return;
            }

            _writer.WriteLine($"Handlers ({handlers.Count}):");
            foreach (var handler in handlers)
            {
                WriteHandlerEntry(handler);
            }
        }

        private void WriteHandlerEntry(JsonElement handler)
        {
            var id = handler.GetStringOrDefault("ConnectionId");
            var name = handler.GetStringOrDefault("Name");

            _writer.WriteLine($"  {_options.FormatIdentifier(name, id)}");
            _writer.WriteLine($"    Executable: {ResolvedOrNotStated(handler, "ExecutablePath")}");
            _writer.WriteLine($"    Mode: {ResolvedOrNotStated(handler, "RunMode")}");
            _writer.WriteLine($"    Reached by: {ResolvedOrNotStated(handler, "Trigger")}");
        }

        // A value the platform resolved, or a plain statement that nothing did. Standing a default in
        // for silence would have this command state something no model says — which is how the run mode
        // came to read as "daemon" for every service whatever it declared.
        private static string ResolvedOrNotStated(JsonElement handler, string field) =>
            handler.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()!
                : "not stated";

        private async Task ListServicesAsync()
        {
            var services = await _mycelium.GetAllServicesAsync();

            if (services.ValueKind != JsonValueKind.Array || services.GetArrayLength() == 0)
            {
                _writer.WriteLine("No running microservices found.");
                return;
            }

            var nameMap = await _resolver.GetGuidToNameMapAsync();

            _writer.WriteLine($"Microservices ({services.GetArrayLength()}):");
            foreach (var service in services.EnumerateArray())
            {
                WriteServiceEntry(service, nameMap);
            }
        }

        private async Task ListAgentsAsync()
        {
            var services = await _mycelium.GetAllServicesAsync();
            var nameMap = await _resolver.GetGuidToNameMapAsync();

            if (services.ValueKind != JsonValueKind.Array || services.GetArrayLength() == 0)
            {
                _writer.WriteLine("No agents found (no registered services).");
                return;
            }

            _writer.WriteLine($"Registered Services ({services.GetArrayLength()}):");
            foreach (var service in services.EnumerateArray())
            {
                WriteServiceEntry(service, nameMap);
            }
        }

        private void WriteServiceEntry(JsonElement service, Dictionary<string, string> nameMap)
        {
            var handlerId = service.GetStringOrDefault("HandlerId");
            var serviceName = service.GetStringOrDefault("ServiceName");
            var endpointUrl = service.GetStringOrDefault("EndpointUrl");
            var healthStatus = service.GetStringOrDefault("HealthStatus", "Unknown");
            var isRunning = service.GetBoolOrDefault("IsRunning");

            var displayName = ResolveDisplayName(serviceName, handlerId, nameMap);

            var status = isRunning ? "Running" : "Stopped";
            var serviceDisplay = _options.FormatIdentifier(displayName, handlerId);
            _writer.WriteLine($"  {serviceDisplay} [{status}]");
            _writer.WriteLine($"    Endpoint: {endpointUrl}");
            _writer.WriteLine($"    Health: {healthStatus}");
        }

        private static string ResolveDisplayName(string serviceName, string handlerId, Dictionary<string, string> nameMap)
        {
            if (!string.IsNullOrEmpty(serviceName) && serviceName != "N/A")
                return serviceName;

            if (handlerId != "N/A" && nameMap.TryGetValue(handlerId.ToLowerInvariant(), out var resolvedName))
                return resolvedName;

            return "N/A";
        }

        private void ShowUsage()
        {
            _writer.WriteLine("Usage: list things [--showguids]    - List all things");
            _writer.WriteLine("       list relations [--showguids] - List all relationships");
            _writer.WriteLine("       list predicates              - List all predicates");
            _writer.WriteLine("       list handlers [--showguids]  - List all handlers");
            _writer.WriteLine("       list services [--showguids]  - List all registered services (with daemon state)");
            _writer.WriteLine("       list agents [--showguids]    - Alias for list services");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
