using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Tributary.Models;

namespace vos.Service.Tributary.Services;

// Tributary as a pipeline step: one call per request. A list of calls on the collection input is run once per item
// by the orchestrator, so each item is fetched, and tried again, on its own.
public sealed class TributaryStep : DagNodeService
{
    public const string CallPort = "call";
    public const string OutcomePort = "outcome";

    private const string NoCall = "Tributary's step takes one call on its 'call' input: the registration, the subject and the address values.";

    private readonly EndpointCallService _calls;
    private readonly PassedOnRequestLimit _passedOnRequestLimit;
    private readonly TimeProvider _clock;

    public TributaryStep(
        IHttpClientFactory httpClientFactory,
        ILogger<TributaryStep> logger,
        string myceliumUrl,
        LaunchToken? launchToken,
        string? apiKey,
        EndpointCallService calls,
        PassedOnRequestLimit passedOnRequestLimit,
        TimeProvider clock)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
    {
        _calls = calls;
        _passedOnRequestLimit = passedOnRequestLimit;
        _clock = clock;
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } =
    [
        PortDescriptor.Input(CallPort, "object", required: true, collection: true),
        PortDescriptor.Output(OutcomePort, "object"),
    ];

    protected override async Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        if (context.Input(CallPort) is not { ValueKind: JsonValueKind.Object } call)
            throw new InvalidOperationException(NoCall);

        var request = call.Deserialize<EndpointCallRequest>(JsonSerializerOptions.Web) ?? throw new InvalidOperationException(NoCall);
        using var deadline = _passedOnRequestLimit.StartDeadline(cancellationToken);
        var answer = StepAnswer.From(await _calls.ExecuteAsync(request, deadline), call, _clock.GetUtcNow());

        if (answer.Succeeded) return NodeResult.Ok((OutcomePort, answer.Outcome));

        var outputs = new Dictionary<string, object?> { [OutcomePort] = answer.Outcome };
        throw answer.CanBeTriedAgain
            ? new FailureCanBeTriedAgainException(answer.Reason!, answer.Wait, outputs)
            : new NodeFailedException(answer.Reason!, outputs);
    }
}
