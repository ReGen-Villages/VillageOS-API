namespace vos.ManagedMicroservice.Phloem.Configuration;

/// <summary>
/// The archetype Thing names the orchestrator resolves by transitive <c>is</c>. Mirrors Mycelium's
/// <c>ServiceModelOptions</c> (PlatformServiceConnection / Service) and extends it with the pipeline vocabulary, so a
/// deployment can rename the model via config alone — no literals in code. Mycelium is the single source:
/// it pushes these to Phloem as launch args when it starts the daemon (see CliArgs); the defaults here
/// only apply when an arg is absent.
///
/// Only archetype names are configurable. The built-in predicates (<c>is</c>/<c>has</c>) and property
/// names (<c>Subdomain</c>, <c>fromPort</c>, …) stay constants in <c>ModelNames</c> — the same granularity
/// Mycelium uses (it hardcodes <c>"Subdomain"</c> and <c>PredicateNames.Is</c>).
/// </summary>
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
