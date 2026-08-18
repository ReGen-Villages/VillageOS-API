namespace vos.Service.Phloem.Model;

/// <summary>The role each archetype plays, as a flag the archetype carries.
///
/// The names used to arrive on the command line: the broker held them in a settings section and spelled
/// every one onto the daemon it started, so two repositories agreed about the model by matching text
/// neither of them could check. Renaming an archetype left this orchestrator starting cleanly and finding
/// nothing (#6516).
///
/// The flags below are the contract instead. A model calls its pipeline archetype whatever suits it and
/// marks it; the snapshot carries the mark, and nothing here reads a name.
/// </summary>
public static class PipelineArchetypes
{
    public const string ConnectionFlag = "__IsConnectionArchetype";
    public const string ServiceFlag = "__IsServiceArchetype";
    public const string PipelineFlag = "__IsPipelineArchetype";
    public const string PipelineNodeFlag = "__IsPipelineNodeArchetype";
    public const string PipelineInputFlag = "__IsPipelineInputArchetype";
    public const string PipelineOutputFlag = "__IsPipelineOutputArchetype";
    public const string PortFlag = "__IsPortArchetype";
    public const string PipelineWireFlag = "__IsPipelineWireArchetype";
    public const string PipelineRunFlag = "__IsPipelineRunArchetype";
    public const string NodeRunFlag = "__IsNodeRunArchetype";

    /// <summary>The roles a DAG is resolved from, which a pipeline snapshot is asked to carry so that an
    /// archetype missing from it means the model marks none rather than that this pipeline uses none.
    ///
    /// The two run roles are not among them: a run Thing is written before the pipeline is loaded, so its
    /// archetype is looked up at the point of writing rather than read off the pipeline.</summary>
    public static readonly IReadOnlyList<string> DagRoleFlags =
    [
        ConnectionFlag, ServiceFlag, PipelineFlag, PipelineNodeFlag,
        PipelineInputFlag, PipelineOutputFlag, PortFlag, PipelineWireFlag,
    ];

    /// <summary>Refuse a model that marks a role played in resolving a DAG on no archetype at all. Left
    /// unmarked, every question this orchestrator asks of the snapshot answers "no", and a pipeline that
    /// is fully described in the model reads as one with nothing in it.</summary>
    public static void RequireRolesAreMarked(PipelineGraph graph)
    {
        var unmarked = DagRoleFlags.Where(flag => graph.ArchetypeCarrying(flag) is null).ToList();
        if (unmarked.Count == 0) return;

        throw new PipelineModelException(
            "The model marks no archetype for: " + string.Join(", ", unmarked)
            + ". Each archetype carries a flag saying which role it plays, and this orchestrator finds "
            + "them that way rather than by name.");
    }
}
