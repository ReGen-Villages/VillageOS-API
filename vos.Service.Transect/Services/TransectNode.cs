using vos.Service.Shared;
using vos.Service.Shared.DagNode;

namespace vos.Service.Transect.Services;

public sealed class TransectNode(
    IHttpClientFactory httpClientFactory, ILogger<TransectNode> logger, string myceliumUrl,
    LaunchToken? launchToken = null, string? apiKey = null)
    : DagNodeService(httpClientFactory, logger, myceliumUrl, launchToken, apiKey)
{
    public const string SubjectPort = "subject";

    public override IReadOnlyList<PortDescriptor> Ports { get; } =
    [
        PortDescriptor.Input(SubjectPort, "object", required: true),
        PortDescriptor.Output(SubjectPort, "object"),
    ];

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken) =>
        throw new NodeFailedException("Transect measures nothing yet.");
}
