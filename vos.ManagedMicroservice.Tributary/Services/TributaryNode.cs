using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Tributary.Models;
using vos.ManagedMicroservice.Shared.DagNode;

namespace vos.ManagedMicroservice.Tributary.Services;

/// <summary>Tributary as a pipeline DAG node (Feature #5628): calls a registered endpoint and emits its
/// response. Runs the identical <see cref="EndpointCallService"/> path as the legacy <c>/handle</c>, so a
/// node gets full parity — auth, paging, and response transforms all driven by the endpoint Thing.</summary>
public sealed class TributaryNode : DagNodeService
{
    private readonly EndpointCallService _endpointCallService;

    public TributaryNode(
        EndpointCallService endpointCallService,
        IHttpClientFactory httpClientFactory,
        ILogger<TributaryNode> logger,
        string myceliumUrl,
        string? serviceToken = null)
        : base(httpClientFactory, logger, myceliumUrl, serviceToken)
    {
        _endpointCallService = endpointCallService;
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } = new[]
    {
        PortDescriptor.Input("endpointName", "string", required: true),
        PortDescriptor.Input("body", "json"),
        PortDescriptor.Input("responseTransform", "string"),
        PortDescriptor.Output("response", "json"),
    };

    protected override async Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var endpointName = context.Input("endpointName")?.GetString()
            ?? throw new InvalidOperationException("Tributary node requires an 'endpointName' input.");

        var request = new EndpointCallRequest { EndpointName = endpointName };
        if (context.Input("body") is { ValueKind: not JsonValueKind.Null } body)
            request.Body = body;
        if (context.Input("responseTransform")?.GetString() is { Length: > 0 } transform)
            request.ResponseTransform = transform;

        var result = await _endpointCallService.ExecuteAsync(request, cancellationToken);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.Error!.Message);

        if (result.Ingest is { } ingest)
            return NodeResult.Ok(
                ("entitiesTouched", ingest.EntitiesTouched),
                ("observationsSubmitted", ingest.ObservationsSubmitted));

        return NodeResult.Ok(("response", result.Content));
    }
}
