using vos.Service.Shared;
using vos.Service.Shared.DagNode;

namespace vos.Service.CSharp.Echo.Services;

// Echo as a pipeline step: copies the message input straight to the echo output. The reference step,
// the smallest thing that shows a step's request and answer working end to end.
public sealed class EchoNode : DagNodeService
{
    public EchoNode(IHttpClientFactory httpClientFactory, ILogger<EchoNode> logger, string myceliumUrl, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
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
