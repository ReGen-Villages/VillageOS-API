namespace vos.Service.Shared.DagNode;

// Thrown by a step whose failure answer carries outputs, so a fail wire leaving it has something to carry.
public class NodeFailedException : Exception
{
    public IReadOnlyDictionary<string, object?> Outputs { get; }

    public NodeFailedException(string message, IReadOnlyDictionary<string, object?>? outputs = null)
        : base(message) => Outputs = outputs ?? NodeResult.Empty.Outputs;
}
