namespace vos.ManagedMicroservice.Phloem.Model;

/// <summary>The fixed model vocabulary the orchestrator reads — the parts that are NOT configurable: the two
/// built-in generic predicates (<c>is</c>, <c>has</c>), property names, and the port-direction values. The
/// configurable <b>archetype</b> names (Connection, Service, Pipeline, …) live in
/// <see cref="vos.ManagedMicroservice.Phloem.Configuration.PipelineModelOptions"/>, pushed from Mycelium's
/// config. This is the same granularity Mycelium uses (it hardcodes <c>"Subdomain"</c> and
/// <c>PredicateNames.Is</c> but takes archetype names from config).</summary>
public static class ModelNames
{
    // Built-in generic predicates
    public const string Is = "is";
    public const string Has = "has";

    // Properties
    public const string Subdomain = "Subdomain";
    public const string Direction = "direction";
    public const string PortType = "type";
    public const string PortName = "portName";
    public const string Required = "required";
    public const string FromPort = "fromPort";
    public const string ToPort = "toPort";
    // Field-level mapping on a wire (#5874): extract from-path of the upstream output, place at to-path of the
    // downstream input; empty = the whole payload. Several wires into one input deep-merge by their to-paths.
    public const string FromPath = "fromPath";
    public const string ToPath = "toPath";
    // An optional JSONata transform on a wire (#5875): reshape the (from-path-extracted) upstream value before
    // it is placed at the to-path of the downstream input.
    public const string Transform = "transform";
    public const string ParamBindings = "paramBindings";
    public const string Collection = "collection";
    public const string OnItemError = "onItemError";
    // The pipeline's published result — the Output boundary node's collected inputs, stored on the PipelineRun (#5873).
    public const string Result = "result";

    public const string DirectionIn = "in";
    public const string DirectionOut = "out";

    // onItemError values: fail-fast (default) vs collect-partial.
    public const string OnItemErrorFail = "fail";
    public const string OnItemErrorContinue = "continue";
}
