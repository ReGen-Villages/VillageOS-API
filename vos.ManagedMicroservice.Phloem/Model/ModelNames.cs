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
    public const string ParamBindings = "paramBindings";
    public const string Collection = "collection";
    public const string OnItemError = "onItemError";

    public const string DirectionIn = "in";
    public const string DirectionOut = "out";

    // onItemError values: fail-fast (default) vs collect-partial.
    public const string OnItemErrorFail = "fail";
    public const string OnItemErrorContinue = "continue";
}
