using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Tributary.Models;

namespace vos.Service.Tributary.Services;

// Tributary as a pipeline step, chosen by the `step` setting on its box. A list on a collection input is run once per
// item by the orchestrator, so each item is fetched or judged, and tried again, on its own.
public sealed class TributaryStep : DagNodeService
{
    public const string StepSetting = "step";
    public const string Fetch = "fetch";
    public const string HistoryChanged = "historyChanged";

    public const string CallPort = "call";
    public const string OutcomePort = "outcome";
    public const string SubjectPort = "subject";

    private const string NoCall = "Tributary's step takes one call on its 'call' input: the registration, the subject and the address values.";
    private const string NoSubject = "Tributary's historyChanged step takes the Thing on its 'subject' input: its id, or an object naming it by \"id\".";

    private sealed record StepKind(string Name, string Title, IReadOnlyList<PortDescriptor> Ports);

    private static readonly IReadOnlyList<StepKind> Kinds =
    [
        new(Fetch, "fetch", [PortDescriptor.Input(CallPort, "object", required: true, collection: true), PortDescriptor.Output(OutcomePort, "object")]),
        new(HistoryChanged, "history changed",
            [PortDescriptor.Input(SubjectPort, "object", required: true, collection: true), PortDescriptor.Output(SubjectPort, "object")]),
    ];

    private readonly EndpointCallService _calls;
    private readonly IEndpointMyceliumClient _broker;
    private readonly PassedOnRequestLimit _passedOnRequestLimit;
    private readonly TimeProvider _clock;

    public TributaryStep(
        IHttpClientFactory httpClientFactory,
        ILogger<TributaryStep> logger,
        string myceliumUrl,
        LaunchToken? launchToken,
        string? apiKey,
        EndpointCallService calls,
        IEndpointMyceliumClient broker,
        PassedOnRequestLimit passedOnRequestLimit,
        TimeProvider clock)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
    {
        _calls = calls;
        _broker = broker;
        _passedOnRequestLimit = passedOnRequestLimit;
        _clock = clock;
    }

    // Every port any step declares. A box is declared from one step's manifest, which ManifestOf answers.
    public override IReadOnlyList<PortDescriptor> Ports { get; } = Kinds.SelectMany(kind => kind.Ports).Distinct().ToList();

    public static IReadOnlyList<PortDescriptor> FetchPorts { get; } = Kinds.Single(kind => kind.Name == Fetch).Ports;

    public static object? ManifestOf(string step) => Kinds.FirstOrDefault(kind => kind.Name == step) is { } kind
        ? new
        {
            service = $"Tributary {kind.Title}",
            ports = kind.Ports,
            settings = new[] { new { name = StepSetting, required = true, values = new[] { kind.Name }, @default = kind.Name } },
        }
        : null;

    // A box naming no step is fetched only until the drawings name every step (Story 8508 removes it).
    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken) =>
        (context.Param(StepSetting) is { ValueKind: JsonValueKind.String } step ? step.GetString() : null) switch
        {
            null or Fetch => FetchAsync(context, cancellationToken),
            HistoryChanged => AskForTheHistoryToBeJudgedAsync(context, cancellationToken),
            var named => throw new InvalidOperationException(
                $"A Tributary box names its step in the '{StepSetting}' setting, one of: {string.Join(", ", Kinds.Select(kind => kind.Name))}; " +
                $"'{named}' is not one."),
        };

    private async Task<NodeResult> FetchAsync(NodeContext context, CancellationToken cancellationToken)
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

    private async Task<NodeResult> AskForTheHistoryToBeJudgedAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var subject = context.Input(SubjectPort);
        var thingId = ThingIdOf(subject) ?? throw new InvalidOperationException(NoSubject);

        using var deadline = _passedOnRequestLimit.StartDeadline(cancellationToken);
        BrokerAnswer answer;
        try
        {
            answer = await _broker.ReportHistoryChangedAsync(thingId, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.HasPassed)
        {
            throw new FailureCanBeTriedAgainException(
                $"The broker had not judged the history of {thingId} within the time the platform allows a passed-on request.");
        }

        if (answer.Accepted) return NodeResult.Ok((SubjectPort, subject));

        var reason = $"The broker did not judge the history of {thingId}: {answer.Refusal}";
        throw answer.CanBeTriedAgain
            ? new FailureCanBeTriedAgainException(reason, answer.Wait)
            : new NodeFailedException(reason);
    }

    private static Guid? ThingIdOf(JsonElement? subject)
    {
        var id = subject is { ValueKind: JsonValueKind.Object } thing && thing.TryGetProperty("id", out var named) ? named : subject;
        return id is { ValueKind: JsonValueKind.String } text && text.TryGetGuid(out var thingId) ? thingId : null;
    }
}
