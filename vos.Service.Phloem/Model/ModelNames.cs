namespace vos.Service.Phloem.Model;

// The fixed model vocabulary the orchestrator reads: the built-in generic predicates (is, has, of, binds),
// property names, and the port-direction values. These are the platform's own words, the same ones
// Mycelium spells out for itself. Which Thing plays which role is not among them — that is read from the
// role Thing that binds it, in PipelineRoles.
public static class ModelNames
{
    public const string Is = "is";
    public const string Has = "has";
    public const string Of = "of";
    // A role Thing reaches the Thing playing it along this.
    public const string Binds = "binds";

    public const string Subdomain = "Subdomain";
    public const string Direction = "direction";
    public const string PortType = "type";
    public const string PortName = "portName";
    public const string Required = "required";
    // Field-level mapping on a wire: extract from-path of the upstream output, place at to-path of the
    // downstream input; empty = the whole payload. Several wires into one input deep-merge by their to-paths.
    public const string FromPath = "fromPath";
    public const string ToPath = "toPath";
    // An optional JSONata transform on a wire: reshape the (from-path-extracted) upstream value before
    // it is placed at the to-path of the downstream input.
    public const string Transform = "transform";
    public const string ParameterBindings = "parameterBindings";
    public const string Collection = "collection";
    // An out port whose answers are Things: the node run is related to each.
    public const string Produces = "produces";
    public const string OnItemError = "onItemError";
    // The pipeline's published result — the Output boundary node's collected inputs, stored on the PipelineRun.
    public const string Result = "result";
    // The run's record: its parameters as given, and when it and each node run started and finished. A
    // node run also keeps its outputs, which a resumed run reads back, and its attempts.
    public const string Parameters = "parameters";
    public const string Status = "status";
    public const string NodeId = "nodeId";
    // A fan-out's node run for one item carries its index and how many items there were.
    public const string Index = "index";
    public const string Total = "total";
    public const string StartedUtc = "startedUtc";
    public const string FinishedUtc = "finishedUtc";
    public const string Outputs = "outputs";
    public const string StartedRun = "startedRun";
    // On a node, how many attempts its policy allows; on a node run, how many were made. An attempt carries its number.
    public const string Attempts = "attempts";
    public const string Number = "number";
    public const string PauseMinutes = "pauseMinutes";
    public const string PauseGrowth = "pauseGrowth";
    public const string PauseSpread = "pauseSpread";
    public const string TryingMinutes = "tryingMinutes";
    // A pipeline the model starts is published; any other is a draft, run only as a rehearsal. A service that
    // rehearses writes nothing but its answer, so a rehearsal may call it; a run and the envelope say rehearsal.
    public const string Published = "published";
    public const string Rehearses = "rehearses";
    public const string Rehearsal = "rehearsal";
    // On a stop request: the sentence a person wrote, which each run it stops records as its error.
    public const string Reason = "reason";
    // On a cadence: the model minutes between its runs, and the instant the last run it started was due. A run a
    // cadence starts carries that instant as its parameter `due`.
    public const string IntervalMinutes = "intervalMinutes";
    public const string LastDue = "lastDue";
    public const string Due = "due";
    // The broker's request-log entry that started a run. A plain identifier: what it names is outside the model.
    public const string RequestId = "requestId";
    public const string Payload = "payload";
    public const string Error = "error";

    public const string DirectionIn = "in";
    public const string DirectionOut = "out";

    // onItemError values: fail-fast (default) vs collect-partial.
    public const string OnItemErrorFail = "fail";
    public const string OnItemErrorContinue = "continue";
}
