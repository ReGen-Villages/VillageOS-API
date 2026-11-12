namespace vos.Service.Shared.DagNode;

// Thrown by a step whose failure came from something outside it that may be back later, such as a provider that was
// down; the orchestrator then calls the step again by its retry policy.
public sealed class FailureCanBeTriedAgainException : Exception
{
    public TimeSpan? Wait { get; }
    public IReadOnlyDictionary<string, object?> Outputs { get; }

    public FailureCanBeTriedAgainException(string message, TimeSpan? wait = null, IReadOnlyDictionary<string, object?>? outputs = null)
        : base(message)
    {
        Wait = wait;
        Outputs = outputs ?? NodeResult.Empty.Outputs;
    }
}
