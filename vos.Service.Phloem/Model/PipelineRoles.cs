using vos.Service.Shared.Roles;

namespace vos.Service.Phloem.Model;

// Each role is a Thing the model declares, and it binds the one Thing playing it along `binds`. A role's identifier
// is the standard version 5 identifier of its word under RoleIdentity.Namespace, so every seed declares the same role
// and a model may call the Thing playing it whatever suits it.
public static class PipelineRoles
{
    public static readonly Role Pipeline = new("pipeline", new("f8bc9986-6eef-54d9-9940-4aca2497109b"));
    public static readonly Role PipelineNode = new("pipelineNode", new("f273f058-e3d6-5a20-aa37-34638ce64735"));
    public static readonly Role PipelineInput = new("pipelineInput", new("634f9ae3-2cca-5dd9-84aa-fec4dff07a6d"));
    public static readonly Role PipelineOutput = new("pipelineOutput", new("880d2158-25a8-5b2b-8b6a-923f21ca153d"));
    public static readonly Role Port = new("port", new("318d6995-e07b-5c83-acb5-86cd935ba4c6"));
    public static readonly Role PipelineWire = new("pipelineWire", new("adfe0071-bbaf-5e7f-abc3-255c08d8735a"));
    public static readonly Role InputConnector = new("inputConnector", new("1f13d260-b2cc-5757-a475-7eb9b7d2323f"));
    public static readonly Role OutputConnector = new("outputConnector", new("7788a338-5eb6-5733-a208-0cf90b4269dd"));
    public static readonly Role PipelineRun = new("pipelineRun", new("b682b68f-0101-5ab4-a7a9-13c3e43df873"));
    public static readonly Role NodeRun = new("nodeRun", new("ca731d14-5acb-5236-b0da-fe8b0836d0d7"));
    public static readonly Role Connection = new("connection", new("d5a3c29f-67c1-55a5-b0e4-9241378bd08d"));
    public static readonly Role Service = new("service", new("768bb2e8-e738-5345-9126-c23ad1513197"));
    // An end node standing for an external system sends that system what reached it, as a sent message.
    public static readonly Role ExternalSystem = new("externalSystem", new("03164f47-30c0-5984-9215-a81317000ec5"));
    public static readonly Role SentMessage = new("sentMessage", new("8807f1eb-543b-5098-93ff-9ce70eb85308"));

    // A boundary node stands for the Thing a run comes from or is left with: a connection, a state, an external
    // system, a kind of message.
    public static readonly Role StandsFor = new("standsFor", new("4277647a-93f8-5860-8717-da2a12673dbf"));
    // A connection reaches the pipeline its trigger starts, kept so a model that relates the connection to its
    // pipeline directly runs here unchanged.
    public static readonly Role PipelineStart = new("pipelineStart", new("871174ab-c8e4-5712-81e1-b95accaea5ce"));
    public static readonly Role RunSubject = new("runSubject", new("b0ca501e-b643-5197-aa2f-ea26b02ebce4"));
    // A state connection reaches the range it watches: a state row on the page stands for the range, since several
    // connections may watch one state, and the broker calls the connection, so the two meet through this edge.
    public static readonly Role StateWatch = new("stateWatch", new("03590900-eb28-55bf-9731-e9a0c03dce82"));
    // An external system reaches the connection it is told through.
    public static readonly Role ToldThrough = new("toldThrough", new("d94c1844-76e6-56fa-ae38-d10405d52826"));
    // An in port reaches the Things it offers, and a node reaches the one it chose, along the same predicate.
    public static readonly Role Choice = new("choice", new("043b23a9-d2e8-5a8a-b81c-1a56bf0f525a"));
    public static readonly Role ProducedThing = new("producedThing", new("ea6e3988-2f5c-557e-818d-408bb7a2edd5"));

    // A node that waits reaches the state it waits for along completionRange, and the in port whose Thing must
    // enter that state along awaitsThrough.
    public static readonly Role CompletionRange = new("completionRange", new("8855f3e8-6717-542a-9d49-2d91880d2751"));
    public static readonly Role AwaitsThrough = new("awaitsThrough", new("4dd13e97-1305-566e-a922-7333e514aa64"));
    // A waiting node run reaches the Thing it waits for and the vigil placed for it; the vigil names the
    // runResumption connection, so its answer resumes the run.
    public static readonly Role AwaitedThing = new("awaitedThing", new("6548fbdd-3474-5043-9864-abb665bb2d4b"));
    public static readonly Role HeldVigil = new("heldVigil", new("6bd74ab0-4d87-5dd8-be69-6f12cceb80ee"));
    public static readonly Role RunResumption = new("runResumption", new("50c894ed-a33d-56cc-a5a6-92aa82d32f4f"));
    // A run started by another run's end reaches the run that started it.
    public static readonly Role StartingRun = new("startingRun", new("9dd7221e-f438-5ccc-b89d-25c9cd56f45c"));
    // One call of a node's service, held by the node run that made it; and the pipeline a node hands its subject
    // to when its trying ends without an answer.
    public static readonly Role Attempt = new("attempt", new("d3014b77-c8f2-51d8-bb74-098c26296944"));
    public static readonly Role HandsOverTo = new("handsOverTo", new("5ee3e03b-c8a6-5ed2-83c7-77448c913175"));
    public static readonly Role Cadence = new("cadence", new("f3883eaf-4e52-5c0c-887b-1bc696578812"));
    // A stop request reaches a run, or any Thing whose runs it stops, along the orchestrator's own runStopping
    // connection; each run stopped reaches the request along stoppedBy.
    public static readonly Role StopRequest = new("stopRequest", new("159ef90f-8089-5055-b5a9-8839d5e28e7a"));
    public static readonly Role RunStopping = new("runStopping", new("7acbab6c-58f3-5145-a9c0-18a907197131"));
    public static readonly Role StoppedBy = new("stoppedBy", new("65242d23-fbb9-5110-b014-0facdbe50b3f"));

    // The roles a DAG is resolved from. The run roles are not among them: a run Thing is written before the
    // pipeline is loaded, so its archetype is looked up at the point of writing.
    public static readonly IReadOnlyList<Role> DagRoles =
        [Connection, Service, Pipeline, PipelineNode, PipelineInput, PipelineOutput, Port, PipelineWire, InputConnector, OutputConnector];

    // Left unbound, every question this orchestrator asks of the drawing answers "no", and a pipeline fully
    // described in the model would read as one with nothing in it.
    public static void RequireRolesAreBound(PipelineGraph graph)
    {
        var unbound = DagRoles.Where(role => graph.Bound(role) is null).Select(role => role.Word).ToList();
        if (unbound.Count == 0) return;

        throw new PipelineModelException(
            "The model binds no Thing to the role" + (unbound.Count == 1 ? " " : "s ") + string.Join(", ", unbound)
            + ". Each role is a Thing the model declares, binding the Thing that plays it along `binds`; this "
            + "orchestrator finds every role that way rather than by name.");
    }
}
