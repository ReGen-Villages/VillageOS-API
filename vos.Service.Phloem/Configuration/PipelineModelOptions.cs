namespace vos.Service.Phloem.Configuration;

// The archetype Thing names the orchestrator resolves by transitive is. Mirrors Mycelium's
// ServiceModelOptions (PlatformServiceConnection / Service) and extends it with the pipeline vocabulary, so a
// deployment can rename the model via config alone — no literals in code. Mycelium is the single source:
// it pushes these to Phloem as launch settings when it starts the daemon (see PhloemLaunchSettings);
// the defaults here only apply when a setting is absent.
// Only archetype names are configurable. The built-in predicates (is/has) and property
// names (Subdomain, fromPort, …) stay constants in ModelNames — the same granularity
// Mycelium uses (it hardcodes "Subdomain" and PredicateNames.Is).
public sealed class PipelineModelOptions
{
    public string Connection { get; init; } = "PlatformServiceConnection";
    public string Service { get; init; } = "Service";
    public string Pipeline { get; init; } = "Pipeline";
    public string PipelineNode { get; init; } = "PipelineNode";
    // Boundary nodes (#5873): a pipeline's external input ("from the start") and output ("at the end"). Each
    // is-a PipelineNode too, so the existing node collection picks them up; the distinct archetype tells the
    // builder to treat them as a param source / result sink rather than a dispatchable service node.
    public string PipelineInput { get; init; } = "PipelineInput";
    public string PipelineOutput { get; init; } = "PipelineOutput";
    public string Port { get; init; } = "Port";
    public string PipelineWire { get; init; } = "PipelineWire";
    public string PipelineRun { get; init; } = "PipelineRun";
    public string NodeRun { get; init; } = "NodeRun";
}
