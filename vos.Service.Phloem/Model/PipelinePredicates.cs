namespace vos.Service.Phloem.Model;

// The role each predicate plays, as a flag the predicate Thing itself carries. A wire's predicate is found
// through the archetype it `is`; these are found on the predicate directly, the way the platform marks
// the predicates it dispatches on, so a model may call each of them whatever suits it.
public static class PipelinePredicates
{
    // A boundary node stands for the Thing a run comes from or is left with: a connection, a state, an
    // external system, a kind of message.
    public const string StandsForFlag = "__IsStandsForPredicate";

    // A connection reaches the pipeline its trigger starts: a second way of binding, kept so a model that
    // relates the connection to its pipeline directly runs here unchanged.
    public const string PipelineStartFlag = "__IsPipelineStartPredicate";

    // A run reaches the Thing whose entry into a state started it.
    public const string RunSubjectFlag = "__IsRunSubjectPredicate";
}
