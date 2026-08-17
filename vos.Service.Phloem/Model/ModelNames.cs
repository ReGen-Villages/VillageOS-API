namespace vos.Service.Phloem.Model;

// The fixed model vocabulary the orchestrator reads: the built-in generic predicates (is, has, of),
// property names, and the port-direction values. These are the platform's own words, the same ones
// Mycelium spells out for itself. Which Thing plays which role is not among them — that is read from the
// flag its archetype carries, in PipelineArchetypes.
public static class ModelNames
{
    // Built-in generic predicates
    public const string Is = "is";
    public const string Has = "has";
    public const string Of = "of";

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
