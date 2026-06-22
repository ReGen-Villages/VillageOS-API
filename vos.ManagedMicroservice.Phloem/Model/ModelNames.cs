namespace vos.ManagedMicroservice.Phloem.Model;

/// <summary>The model vocabulary the orchestrator reads. Archetype names are matched via transitive
/// <c>is</c> (<see cref="PipelineGraph.IsOfType"/>) — config-named, never predicate-name literals. The two
/// built-in generic predicates (<c>is</c>, <c>has</c>) are the only predicates referenced by name; a pipeline
/// wire is found by its predicate Thing being <c>is PipelineWire</c>. Mirrors Mycelium's ServiceModelOptions
/// (Connection / Service) so the same instances resolve.</summary>
public static class ModelNames
{
    // Built-in generic predicates
    public const string Is = "is";
    public const string Has = "has";

    // Archetypes
    public const string Pipeline = "Pipeline";
    public const string PipelineNode = "PipelineNode";
    public const string Port = "Port";
    public const string Connection = "Connection";
    public const string Service = "Service";
    public const string PipelineWire = "PipelineWire";
    public const string PipelineRun = "PipelineRun";
    public const string NodeRun = "NodeRun";

    // Properties
    public const string Subdomain = "Subdomain";
    public const string Direction = "direction";
    public const string PortType = "type";
    public const string PortName = "portName";
    public const string Required = "required";
    public const string FromPort = "fromPort";
    public const string ToPort = "toPort";

    public const string DirectionIn = "in";
    public const string DirectionOut = "out";
}
