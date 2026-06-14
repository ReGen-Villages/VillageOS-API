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

                    case "daemons":
                        await ListDaemonsAsync();
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
                _writer.WriteLine("Error: " + ex.Message);
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

            var modelName = await GetModelNameAsync();
            _writer.WriteLine($"Model: {modelName}");
            _writer.WriteLine($"Things ({things.GetArrayLength()}):");

            foreach (var thing in things.EnumerateArray())
            {
                WriteThingEntry(thing);
            }
        }

        private void WriteThingEntry(JsonElement thing)
        {
            var id = thing.GetStringOrDefault("Id");
            var name = thing.GetStringOrDefault("Name");
            _writer.WriteLine($"  {_options.FormatIdentifier(name, id)}");

            WriteProperties(thing, "    ");

            if (!thing.HasObjectProperty("InheritedProperties")) return;

            var inherited = thing.GetProperty("InheritedProperties");
            foreach (var sourceEntry in inherited.EnumerateObject())
            {
                var sourceName = sourceEntry.Value.GetStringOrDefault("SourceName", "unknown");
                WriteInheritedProperties(sourceEntry.Value, sourceName, "    ");
            }
        }

        private void WriteInheritedProperties(JsonElement inheritedSet, string sourceName, string indent)
        {
            var stack = new Stack<(JsonElement Element, string Source)>();
            stack.Push((inheritedSet, sourceName));

            while (stack.Count > 0)
            {
                var (element, source) = stack.Pop();

                WritePropertiesFromElement(element, source, indent);
                PushNestedInheritance(stack, element, source);
            }
        }

        private void WritePropertiesFromElement(JsonElement element, string source, string indent)
        {
            if (!element.TryGetProperty("Properties", out var props) || props.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in props.EnumerateObject())
            {
                var valueStr = prop.Value.FormatPropertyValue();
                _writer.WriteLine($"{indent}{source}.{prop.Name}: {valueStr} (inherited)");
            }
        }

        private static void PushNestedInheritance(Stack<(JsonElement Element, string Source)> stack, JsonElement element, string source)
        {
            if (!element.TryGetProperty("Inherited", out var nested) || nested.ValueKind != JsonValueKind.Object)
                return;

            foreach (var nestedEntry in nested.EnumerateObject())
            {
                var nestedSource = GetSourceName(nestedEntry.Value, nestedEntry.Name);
                stack.Push((nestedEntry.Value, $"{source}.{nestedSource}"));
            }
        }

        private static string GetSourceName(JsonElement element, string fallback)
        {
            return element.TryGetProperty("SourceName", out var nameProp)
                ? nameProp.GetString() ?? fallback
                : fallback;
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
            var name = rel.GetStringOrDefault("Name");
            var subjectId = rel.GetStringOrDefault("SubjectId");
            var targetId = rel.GetStringOrDefault("TargetId");
            var predicateId = rel.GetStringOrDefault("PredicateId");

            var subjectName = ResolveName(subjectId, nameMap);
            var targetName = ResolveName(targetId, nameMap);
            var predicateName = ResolveName(predicateId, nameMap);

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

        private async Task ListHandlersAsync()
        {
            var things = await _mycelium.GetAllThingsAsync();

            if (things.ValueKind != JsonValueKind.Array)
            {
                _writer.WriteLine("No handlers found.");
                return;
            }

            var handlers = new List<JsonElement>();
            foreach (var thing in things.EnumerateArray())
            {
                if (thing.TryGetProperty("Properties", out var props) &&
                    props.TryGetProperty("ExecutablePath", out _))
                {
                    handlers.Add(thing);
                }
            }

            if (handlers.Count == 0)
            {
                _writer.WriteLine("No handlers found. Handlers are things with an 'ExecutablePath' property.");
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
            var id = handler.GetStringOrDefault("Id");
            var name = handler.GetStringOrDefault("Name");
            var props = handler.GetProperty("Properties");
            var execPath = props.GetStringOrDefault("ExecutablePath");
            var runMode = props.GetStringOrDefault("RunMode", "daemon");

            _writer.WriteLine($"  {_options.FormatIdentifier(name, id)}");
            _writer.WriteLine($"    Executable: {execPath}");
            _writer.WriteLine($"    Mode: {runMode}");
        }

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

        private async Task ListDaemonsAsync()
        {
            var daemons = await _mycelium.GetAllDaemonsAsync();

            if (daemons.ValueKind != JsonValueKind.Array || daemons.GetArrayLength() == 0)
            {
                _writer.WriteLine("No tracked daemons found.");
                return;
            }

            _writer.WriteLine($"Daemons ({daemons.GetArrayLength()}):");
            foreach (var daemon in daemons.EnumerateArray())
            {
                WriteDaemonEntry(daemon);
            }
        }

        private void WriteDaemonEntry(JsonElement daemon)
        {
            var key = daemon.GetStringOrDefault("Key", "unknown");
            var isRunning = daemon.GetBoolOrDefault("IsRunning");
            var processId = daemon.GetNullableInt("ProcessId")?.ToString() ?? "N/A";
            var failures = daemon.GetIntOrDefault("ConsecutiveFailures");
            var lastFailure = daemon.GetNullableDateTime("LastFailureTime")?.ToString("g") ?? "N/A";

            var status = isRunning ? "Running" : "Stopped";
            _writer.WriteLine($"  {key} [{status}]");
            _writer.WriteLine($"    Process ID: {processId}");
            if (failures > 0)
            {
                _writer.WriteLine($"    Consecutive Failures: {failures}");
                _writer.WriteLine($"    Last Failure: {lastFailure}");
            }
        }

        private async Task ListAgentsAsync()
        {
            var services = await _mycelium.GetAllServicesAsync();
            var daemons = await _mycelium.GetAllDaemonsAsync();
            var nameMap = await _resolver.GetGuidToNameMapAsync();

            var hasServices = services.ValueKind == JsonValueKind.Array && services.GetArrayLength() > 0;
            var hasDaemons = daemons.ValueKind == JsonValueKind.Array && daemons.GetArrayLength() > 0;

            if (!hasServices && !hasDaemons)
            {
                _writer.WriteLine("No agents found (no services or daemons).");
                return;
            }

            if (hasServices)
            {
                _writer.WriteLine($"Registered Services ({services.GetArrayLength()}):");
                foreach (var service in services.EnumerateArray())
                {
                    WriteServiceEntry(service, nameMap);
                }
            }

            if (hasDaemons)
            {
                if (hasServices) _writer.WriteLine();
                _writer.WriteLine($"Lazy-started Daemons ({daemons.GetArrayLength()}):");
                foreach (var daemon in daemons.EnumerateArray())
                {
                    WriteDaemonEntry(daemon);
                }
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
            _writer.WriteLine("       list services [--showguids]  - List all running microservices");
            _writer.WriteLine("       list daemons                 - List all tracked daemons");
            _writer.WriteLine("       list agents [--showguids]    - List all agents (services + daemons)");
            _writer.WriteLine();
            _writer.WriteLine("Options:");
            _writer.WriteLine("  --showguids, -g  Show GUIDs in addition to names");
        }
    }
}
