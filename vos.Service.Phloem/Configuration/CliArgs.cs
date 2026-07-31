namespace vos.Service.Phloem.Configuration;

// Standard managed-microservice launch args (mirrors the other services), plus the model archetype
// names Mycelium pushes from its ServiceModel config so the orchestrator's vocabulary stays in sync
// with the broker. The pipeline to run arrives per request, not at startup.
public record CliArgs(
    int Port,
    string MyceliumUrl,
    string? Token = null,
    string? SigningKey = null,
    string? Issuer = null,
    string? Audience = null)
{
    // Archetype names resolved from --*Archetype= args (each falls back to its default).
    public PipelineModelOptions Model { get; init; } = new();

    // Returns null if required args are missing or invalid.
    public static CliArgs? Parse(string[] args)
    {
        var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
        var myceliumUrlArg = args.FirstOrDefault(a => a.StartsWith("--myceliumUrl="));

        if (portArg == null || myceliumUrlArg == null)
            return null;
        if (!int.TryParse(portArg.Substring("--port=".Length), out var port) || port < 1 || port > 65535)
            return null;

        var defaults = new PipelineModelOptions();
        var model = new PipelineModelOptions
        {
            Connection = Arg(args, "connectionArchetype") ?? defaults.Connection,
            Service = Arg(args, "serviceArchetype") ?? defaults.Service,
            Pipeline = Arg(args, "pipelineArchetype") ?? defaults.Pipeline,
            PipelineNode = Arg(args, "pipelineNodeArchetype") ?? defaults.PipelineNode,
            PipelineInput = Arg(args, "pipelineInputArchetype") ?? defaults.PipelineInput,
            PipelineOutput = Arg(args, "pipelineOutputArchetype") ?? defaults.PipelineOutput,
            Port = Arg(args, "portArchetype") ?? defaults.Port,
            PipelineWire = Arg(args, "pipelineWireArchetype") ?? defaults.PipelineWire,
            PipelineRun = Arg(args, "pipelineRunArchetype") ?? defaults.PipelineRun,
            NodeRun = Arg(args, "nodeRunArchetype") ?? defaults.NodeRun,
        };

        return new CliArgs(
            port,
            myceliumUrlArg.Substring("--myceliumUrl=".Length),
            Arg(args, "token"),
            Arg(args, "signingKey"),
            Arg(args, "issuer"),
            Arg(args, "audience"))
        {
            Model = model,
        };
    }

    private static string? Arg(string[] args, string name)
    {
        var prefix = $"--{name}=";
        var match = args.FirstOrDefault(a => a.StartsWith(prefix));
        return match?.Substring(prefix.Length);
    }

    public static string UsageMessage =>
        "Usage: dotnet run -- --port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] " +
        "[--issuer=<iss>] [--audience=<aud>] [--connectionArchetype=<name>] [--serviceArchetype=<name>] " +
        "[--pipelineArchetype=<name>] [--pipelineNodeArchetype=<name>] [--pipelineInputArchetype=<name>] " +
        "[--pipelineOutputArchetype=<name>] [--portArchetype=<name>] " +
        "[--pipelineWireArchetype=<name>] [--pipelineRunArchetype=<name>] [--nodeRunArchetype=<name>]";
}
