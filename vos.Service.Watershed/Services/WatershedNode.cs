using vos.Service.Shared;
using vos.Service.Shared.DagNode;

namespace vos.Service.Watershed.Services;

public sealed class WatershedNode : DagNodeService
{
    public const string SubjectPort = "subject";
    public const string StampProperty = "groundWorkedOutAt";

    public static readonly TimeSpan ReadBackEvery = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ReadBackForAtMost = TimeSpan.FromSeconds(15);

    private readonly TimeProvider _clock;
    private readonly GroundHeightReader _heights;

    public WatershedNode(
        IHttpClientFactory httpClientFactory, ILogger<WatershedNode> logger, string myceliumUrl, GroundHeightReader heights,
        TimeProvider clock, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey)
    {
        _heights = heights;
        _clock = clock;
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = [];

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
