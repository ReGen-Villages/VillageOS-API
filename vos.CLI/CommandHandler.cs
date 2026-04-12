namespace vos.CLI
{
    public class CommandHandler
    {
        private readonly TextReader _reader;
        private readonly TextWriter _writer;
        private readonly BrokerClient _broker;
        private readonly string _brokerUrl;
        private readonly bool _interactiveMode;

        public CommandHandler(TextReader reader, TextWriter writer, BrokerClient broker, string brokerUrl, bool interactiveMode = false)
        {
            _reader = reader;
            _writer = writer;
            _broker = broker;
            _brokerUrl = brokerUrl;
            _interactiveMode = interactiveMode;
        }

        public void ShowHelp()
        {
            _writer.WriteLine("Available commands:");
            _writer.WriteLine();
            _writer.WriteLine("Basic Operations:");
            _writer.WriteLine("  create thing <name>                         - Create a new thing");
            _writer.WriteLine("  create property <thing> <name> <type> <val> - Add a property to a thing");
            _writer.WriteLine("  create rel-property <relId> <name> <type> <val> - Add a property to a relationship");
            _writer.WriteLine("  create relation <subj> <pred> <target>      - Add a relationship");
            _writer.WriteLine("  delete thing <thing>                        - Delete a thing");
            _writer.WriteLine("  delete relationship <id>                    - Delete a relationship by ID");
            _writer.WriteLine("  delete property <thing> <name>              - Delete a property from a thing");
            _writer.WriteLine("  delete rel-property <relId> <name>          - Delete a property from a relationship");
            _writer.WriteLine("  set <thing> <name> <value>                  - Set a property value");
            _writer.WriteLine();
            _writer.WriteLine("Querying:");
            _writer.WriteLine("  get thing <thing>                           - Get a thing");
            _writer.WriteLine("  find thing <pattern>                        - Find things by name pattern");
            _writer.WriteLine("  find relationships <thing>                  - Find all relationships for a thing");
            _writer.WriteLine("  list things                                 - List all things");
            _writer.WriteLine("  list relations                              - List all relationships");
            _writer.WriteLine("  list predicates                             - List all predicates");
            _writer.WriteLine("  list services                               - List all running microservices");
            _writer.WriteLine("  list daemons                                - List all tracked daemons");
            _writer.WriteLine();
            _writer.WriteLine("Advanced Queries:");
            _writer.WriteLine("  query property <name> <value>               - Find things with a property value");
            _writer.WriteLine("  query predicate <name>                      - Find relationships by predicate");
            _writer.WriteLine("  query stats                                 - Show model statistics");
            _writer.WriteLine();
            _writer.WriteLine("Temporal Queries:");
            _writer.WriteLine("  temporal snapshot [timestamp]               - Get model snapshot at a point in time");
            _writer.WriteLine("  temporal at <thing> <timestamp>             - Get thing state at a specific time");
            _writer.WriteLine("  temporal history <thing> <prop> [start] [end] - Show property change history");
            _writer.WriteLine("  temporal mutations [target] [start] [end]   - Show property mutations");
            _writer.WriteLine("    - mutations                               Show all mutations across the model");
            _writer.WriteLine("    - mutations model                         Same as above");
            _writer.WriteLine("    - mutations <name>                        Show mutations for a thing by name or ID");
            _writer.WriteLine("    - mutations thing <name>                  Show mutations for a thing");
            _writer.WriteLine("    - mutations rel <id>                      Show mutations for a relationship");
            _writer.WriteLine();
            _writer.WriteLine("Expected Ranges & States:");
            _writer.WriteLine("  range create <thing> <name> <criteria>      - Create an expected range");
            _writer.WriteLine("  range list <thing>                          - List all ranges for a thing");
            _writer.WriteLine("  range get <thing> <name>                    - Get a specific range");
            _writer.WriteLine("  range delete <thing> <name>                 - Delete a range");
            _writer.WriteLine("  range validate <criteria>                   - Validate criteria syntax");
            _writer.WriteLine("  state <thing>                               - Get current states for a thing");
            _writer.WriteLine("  state query <state-name>                    - Find things in a state");
            _writer.WriteLine();
            _writer.WriteLine("File Operations:");
            _writer.WriteLine("  serialize [file]                            - Serialize model to JSON");
            _writer.WriteLine("  seed [file]                                 - Alias for serialize");
            _writer.WriteLine("  deserialize <file>                          - Deserialize model from JSON file");
            _writer.WriteLine("  plant <file> [mode] [options]               - Load seed and set property modes");
            _writer.WriteLine("    Modes: CurrentOnly, RingBuffer, Sampled, FullHistory");
            _writer.WriteLine("    Options: --ringbuffer=N, --samplerate=N");
            _writer.WriteLine("  pwd                                         - Show current directory");
            _writer.WriteLine("  cd <path>                                   - Change current directory");
            _writer.WriteLine();
            _writer.WriteLine("Microservices:");
            _writer.WriteLine("  start service <handler>                     - Start a registered microservice");
            _writer.WriteLine("  stop service <handler>                      - Stop a running microservice");
            _writer.WriteLine("  stop daemon <key>                           - Stop a lazy-started daemon");
            _writer.WriteLine();
            _writer.WriteLine("Configuration:");
            _writer.WriteLine("  config mode                                 - Show property mode configuration");
            _writer.WriteLine("  config mode <ModeName>                      - Set default property mode");
            _writer.WriteLine("  config mode get <thing> <prop>              - Get property mode");
            _writer.WriteLine("  config mode set <thing> <prop> <ModeName>   - Set property mode");
            _writer.WriteLine();
            _writer.WriteLine("Seed Management:");
            _writer.WriteLine("  seeds status                                - Show seed loading status");
            _writer.WriteLine("  seeds list                                  - List available library seeds");
            _writer.WriteLine("  seeds load <name>                           - Load a library seed by name");
            _writer.WriteLine("  seeds save <name>                           - Save current model as a library seed");
            _writer.WriteLine("  seeds reload                                - Reload seeds from disk");
            _writer.WriteLine();
            _writer.WriteLine("Model Management:");
            _writer.WriteLine("  model list                                  - List available models");
            _writer.WriteLine("  model switch <id|name>                      - Switch to a different model");
            _writer.WriteLine("  clear model                                 - Clear all things and relationships");
            _writer.WriteLine();
            _writer.WriteLine("Broker:");
            _writer.WriteLine("  broker status                               - Show broker seed status");
            _writer.WriteLine("  broker endpoints                            - List registered endpoint services");
            _writer.WriteLine();
            _writer.WriteLine("User Management:");
            _writer.WriteLine("  user change-password <userId>               - Change a user's password");
            _writer.WriteLine();
            _writer.WriteLine("Other:");
            _writer.WriteLine("  shutdown                                    - Shut down the broker");
            _writer.WriteLine("  help                                        - Show this help");
            _writer.WriteLine("  exit                                        - Exit the console");
            _writer.WriteLine();
            _writer.WriteLine("Note: <thing>, <subj>, <pred>, <target>, <handler> can be a GUID or a unique name.");
            _writer.WriteLine();
            _writer.WriteLine("Output Options (can be added to most commands):");
            _writer.WriteLine("  --showguids, -g                             - Show GUIDs in addition to names");
        }

        public async Task HandleCommandAsync(string cmd, string? arg)
        {
            var handlers = GetCommandHandlers();
            if (handlers.TryGetValue(cmd, out var handler))
                await handler(cmd, arg ?? string.Empty);
            else
                _writer.WriteLine("Unknown command; type help for list of commands.");
        }

        private async Task ShutdownBrokerAsync()
        {
            try
            {
                var success = await _broker.ShutdownBrokerAsync();
                if (success)
                    _writer.WriteLine("Broker shutdown initiated.");
                else
                    _writer.WriteLine("Failed to initiate broker shutdown.");
            }
            catch (HttpRequestException)
            {
                _writer.WriteLine("Broker shutdown initiated (connection closed).");
            }
        }

        private async Task ClearModelAsync(string arg)
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].ToLowerInvariant() != "model")
            {
                _writer.WriteLine("Usage: clear model");
                return;
            }

            await _broker.ClearModelAsync();
            _writer.WriteLine("Model cleared. All things and relationships have been removed.");
        }

        private Dictionary<string, Func<string, string, Task>> GetCommandHandlers() => new()
        {
            ["cd"] = async (c, a) => await new FileSystemCommandHandler(c, a, _writer, _broker).ExecuteAsync(),
            ["pwd"] = async (c, a) => await new FileSystemCommandHandler(c, a, _writer, _broker).ExecuteAsync(),
            ["deserialize"] = async (c, a) => await new FileSystemCommandHandler(c, a, _writer, _broker).ExecuteAsync(),
            ["plant"] = async (_, a) => await new PlantCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["serialize"] = async (c, a) => await new FileSystemCommandHandler(c, a, _writer, _broker).ExecuteAsync(),
            ["seed"] = async (_, a) => await new FileSystemCommandHandler("serialize", a, _writer, _broker).ExecuteAsync(),
            ["create"] = async (_, a) => await new CreateCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["delete"] = async (_, a) => await new DeleteCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["find"] = async (_, a) => await new FindCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["get"] = async (_, a) => await new GetCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["list"] = async (_, a) => await new ListCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["set"] = async (_, a) => await new SetCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["temporal"] = async (_, a) => await new TemporalCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["query"] = async (_, a) => await new QueryCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["start"] = async (_, a) => await new StartCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["stop"] = async (_, a) => await new StopCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["config"] = async (_, a) => await new ConfigCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["range"] = async (_, a) => await new RangeCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["state"] = async (_, a) => await new StateCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["shutdown"] = async (_, _) => await ShutdownBrokerAsync(),
            ["clear"] = async (_, a) => await ClearModelAsync(a),
            ["seeds"] = async (_, a) => await new SeedCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["broker"] = async (_, a) => await new BrokerStatusCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["model"] = async (_, a) => await new ModelCommandHandler(a, _writer, _broker).ExecuteAsync(),
            ["user"] = async (_, a) => await new UserCommandHandler(a, _reader, _writer, _broker).ExecuteAsync(),
        };

        public async Task RunAsync()
        {
            _writer.WriteLine($"VillageOS CLI - Connected to broker at {_brokerUrl}");
            _writer.WriteLine("Type 'help' to see available commands.");

            // Test broker connection
            try
            {
                await _broker.GetTokenAsync();
                _writer.WriteLine("Successfully authenticated with broker.");
            }
            catch (Exception ex)
            {
                _writer.WriteLine($"Warning: Could not connect to broker: {ex.Message}");
                _writer.WriteLine("Commands will fail until the broker is available.");
            }

            _writer.WriteLine();

            while (true)
            {
                string? input;
                if (_interactiveMode)
                {
                    input = ReadLine.Read("> ");
                    if (!string.IsNullOrWhiteSpace(input))
                        ReadLine.AddHistory(input);
                }
                else
                {
                    _writer.Write("> ");
                    input = _reader.ReadLine();
                }

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var cmd = parts[0].ToLowerInvariant();
                string? arg = parts.Length > 1 ? parts[1] : null;

                if (cmd == "exit")
                    break;

                if (cmd == "help")
                    ShowHelp();
                else
                    try
                    {
                        await HandleCommandAsync(cmd, arg);
                    }
                    catch (HttpRequestException ex)
                    {
                        _writer.WriteLine($"Error communicating with broker: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _writer.WriteLine("Error: " + ex.Message);
                    }
            }
        }
    }
}
