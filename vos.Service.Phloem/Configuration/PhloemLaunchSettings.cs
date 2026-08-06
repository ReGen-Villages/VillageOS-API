using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Phloem.Configuration;

// The common service settings plus the model archetype names Mycelium pushes from its ServiceModel
// config, so the orchestrator's vocabulary stays in step with the broker. The pipeline to run
// arrives per request, not at startup.
public sealed record PhloemLaunchSettings(ServiceLaunchSettings Service, PipelineModelOptions Model)
{
    public static PhloemLaunchSettings? Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var service = ServiceLaunchSettings.Parse(reader);
        if (service is null)
            return null;

        return new PhloemLaunchSettings(service, ReadModel(reader));
    }

    private static PipelineModelOptions ReadModel(LaunchSettingReader reader)
    {
        var defaults = new PipelineModelOptions();

        return new PipelineModelOptions
        {
            Connection = reader.Read("connectionArchetype") ?? defaults.Connection,
            Service = reader.Read("serviceArchetype") ?? defaults.Service,
            Pipeline = reader.Read("pipelineArchetype") ?? defaults.Pipeline,
            PipelineNode = reader.Read("pipelineNodeArchetype") ?? defaults.PipelineNode,
            PipelineInput = reader.Read("pipelineInputArchetype") ?? defaults.PipelineInput,
            PipelineOutput = reader.Read("pipelineOutputArchetype") ?? defaults.PipelineOutput,
            Port = reader.Read("portArchetype") ?? defaults.Port,
            PipelineWire = reader.Read("pipelineWireArchetype") ?? defaults.PipelineWire,
            PipelineRun = reader.Read("pipelineRunArchetype") ?? defaults.PipelineRun,
            NodeRun = reader.Read("nodeRunArchetype") ?? defaults.NodeRun,
        };
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--connectionArchetype=<name>] [--serviceArchetype=<name>] [--pipelineArchetype=<name>] " +
        "[--pipelineNodeArchetype=<name>] [--pipelineInputArchetype=<name>] " +
        "[--pipelineOutputArchetype=<name>] [--portArchetype=<name>] [--pipelineWireArchetype=<name>] " +
        "[--pipelineRunArchetype=<name>] [--nodeRunArchetype=<name>]",
        "\n  --*Archetype   Archetype Thing name for that part of the pipeline model");
}
