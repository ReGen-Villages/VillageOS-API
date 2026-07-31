using vos.Service.Shared.DagNode;

namespace vos.Service.CSharp.Echo.Services;

// Echo as a pipeline DAG node (Feature #5628): copies the message input straight to the
// echo output. The reference node — the smallest thing that proves the envelope end to end.
public sealed class EchoNode : DagNodeService
{
    public EchoNode(IHttpClientFactory httpClientFactory, ILogger<EchoNode> logger, string myceliumUrl, string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
    {
        PortDescriptor.Input("message", "string", required: true),
        PortDescriptor.Output("echo", "string"),
    };

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var message = context.Input("message")?.GetString()
            ?? throw new InvalidOperationException("Echo node requires a 'message' input.");
        return Task.FromResult(NodeResult.Ok(("echo", message)));
    }
}
