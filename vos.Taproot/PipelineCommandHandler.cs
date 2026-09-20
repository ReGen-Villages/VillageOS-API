using System.Text.Json;
using static vos.Taproot.ModelReading;

namespace vos.Taproot;

// `pipeline list`, `pipeline run`, `pipeline cancel` and `pipeline history`. Which Thing is a pipeline
// or a run is read from the flag its archetype carries, the same contract the orchestrator reads, so a
// model may call those archetypes whatever suits it.
public class PipelineCommandHandler : CommandHandlerWithOutputOptions
{
    public const string PipelineFlag = "__IsPipelineArchetype";
    public const string PipelineRunFlag = "__IsPipelineRunArchetype";

    // The orchestrator is reached as the endpoint service at this subdomain, as Trellis reaches it.
    public const string OrchestratorSubdomain = "phloem";

    // The platform's own predicate a run is written through, and the property the orchestrator polls
    // for a cooperative cancellation; neither is a model's vocabulary.
    private const string OfPredicateName = "of";
    private const string CancelRequestedProperty = "cancelRequested";

    public PipelineCommandHandler(string arg, TextWriter writer, MyceliumClient mycelium, OutputOptions? options = null)
        : base(arg, writer, mycelium, options)
    {
    }

    public async Task ExecuteAsync()
    {
        if (!CommandParser.TryParseSubcommand(_arg ?? "", out var subcommand, out var args))
        {
            ShowUsage();
            return;
        }

        try
        {
            switch (subcommand.ToLowerInvariant())
            {
                case "list": await ListAsync(); break;
                case "run": await RunAsync(args); break;
                case "cancel": await CancelAsync(args); break;
                case "history": await HistoryAsync(args); break;
                default: ShowUsage(); break;
            }
        }
        catch (Exception ex)
        {
            _writer.WriteLine("Error: " + OperatorMessage.For(ex));
        }
    }

    private async Task ListAsync()
    {
        var model = await ModelSnapshot.ReadAsync(_mycelium);
        if (OneOwning(model, PipelineFlag) is not { } archetype)
        {
            _writer.WriteLine($"This model marks no archetype with '{PipelineFlag}'.");
            return;
        }

        var pipelines = await _mycelium.GetThingsAsync(new ThingListNarrowing(Type: archetype.Name));
        if (pipelines.GetArrayLength() == 0)
        {
            _writer.WriteLine("No pipelines.");
            return;
        }

        _writer.WriteLine($"Pipelines ({pipelines.GetArrayLength()}):");
        foreach (var pipeline in pipelines.EnumerateArray())
            _writer.WriteLine($"  {_options.FormatIdentifier(pipeline.GetStringOrDefault("Name"), pipeline.GetStringOrDefault("Id"))}");
    }

    private async Task RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            _writer.WriteLine("Usage: pipeline run <pipeline> [json params] [--wait]");
            return;
        }

        var resolved = await _resolver.ResolveThingAsync(args[0]);
        if (!resolved.IsSuccess)
        {
            _writer.WriteLine($"Error: {resolved.ErrorMessage}");
            return;
        }

        var wait = CommandOptions.Has(args, "--wait");
        var parameters = string.Join(" ", args.Skip(1).Where(token => !token.Equals("--wait", StringComparison.OrdinalIgnoreCase)));
        if (parameters.Length == 0) parameters = "{}";
        if (!CommandParser.IsJson(parameters))
        {
            _writer.WriteLine("Error: the parameters are not JSON. Nothing was sent.");
            return;
        }

        var body = $"{{\"pipelineId\":\"{resolved.Id}\",\"params\":{parameters},\"async\":{(wait ? "false" : "true")}}}";
        CommandParser.WriteJsonOrText(_writer, await _mycelium.PostToEndpointAsync(OrchestratorSubdomain, body));
    }

    private async Task CancelAsync(string[] args)
    {
        if (args.Length == 0 || !Guid.TryParse(args[0], out var runId))
        {
            _writer.WriteLine("Usage: pipeline cancel <run-id>");
            return;
        }

        await _mycelium.SetPropertyAsync(runId, CancelRequestedProperty, "string", "true");
        _writer.WriteLine($"Cancellation requested for run {runId}.");
    }

    private async Task HistoryAsync(string[] args)
    {
        if (args.Length == 0)
        {
            _writer.WriteLine("Usage: pipeline history <pipeline>");
            return;
        }

        var resolved = await _resolver.ResolveThingAsync(args[0]);
        if (!resolved.IsSuccess)
        {
            _writer.WriteLine($"Error: {resolved.ErrorMessage}");
            return;
        }

        var model = await ModelSnapshot.ReadAsync(_mycelium);
        if (OneOwning(model, PipelineRunFlag) is not { } runArchetype)
        {
            _writer.WriteLine($"This model marks no archetype with '{PipelineRunFlag}'.");
            return;
        }

        var ofThisPipeline = model.Relationships.EnumerateArray()
            .Where(edge => Target(edge) == resolved.Id && NameOf(model, Predicate(edge)) == OfPredicateName)
            .Select(Subject)
            .ToHashSet();
        var runs = (await _mycelium.GetThingsAsync(new ThingListNarrowing(Type: runArchetype.Name)))
            .EnumerateArray()
            .Select(run => Identifier(run, "Id"))
            .Where(ofThisPipeline.Contains)
            .Select(id => (Id: id, Started: Value(model, id, "startedUtc") ?? "", Status: Value(model, id, "status") ?? ""))
            .OrderByDescending(run => run.Started, StringComparer.Ordinal)
            .ToList();

        var pipelineName = await _resolver.ResolveNameAsync(resolved.Id);
        if (runs.Count == 0)
        {
            _writer.WriteLine($"No runs of {_options.FormatIdentifier(pipelineName, resolved.Id)}.");
            return;
        }

        _writer.WriteLine($"Runs of {_options.FormatIdentifier(pipelineName, resolved.Id)}, newest first:");
        foreach (var run in runs)
            _writer.WriteLine($"  {run.Started,-28} {run.Status,-10} {run.Id}");
    }

    private void ShowUsage()
    {
        _writer.WriteLine("Usage: pipeline list [--showguids]               - Every pipeline the model holds");
        _writer.WriteLine("       pipeline run <pipeline> [json params] [--wait]");
        _writer.WriteLine("                                                 - Start a run; --wait prints the per-node result instead of the run id");
        _writer.WriteLine("       pipeline cancel <run-id>                  - Ask a running pipeline to stop");
        _writer.WriteLine("       pipeline history <pipeline>               - The runs of a pipeline, newest first");
        _writer.WriteLine();
        _writer.WriteLine("<pipeline> can be a GUID or a unique name. Pipelines and runs are found by the flags");
        _writer.WriteLine("the model marks its archetypes with, never by an archetype's name.");
    }
}
