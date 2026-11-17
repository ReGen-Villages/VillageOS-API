using System.Text.Json;
using Microsoft.Extensions.Logging;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;

namespace vos.Service.Forage.Steps;

// Each stage of a discovery run as a pipeline step, chosen by the `step` setting on its box. A failed read of the
// model is answered as a failure that may be tried again, never as an empty answer.
public sealed class ForageSteps : DagNodeService
{
    public const string StepSetting = "step";

    public const string ResolveRegion = "resolveRegion";
    public const string PlanCalls = "planCalls";
    public const string RecordResults = "recordResults";
    public const string ResolveWords = "resolveWords";
    public const string StartAnalysis = "startAnalysis";
    public const string OfferSource = "offerSource";

    private sealed record StepKind(string Name, string Title, IReadOnlyList<PortDescriptor> Ports);

    private static readonly IReadOnlyList<StepKind> Kinds =
    [
        new(ResolveRegion, "resolve region", [PortDescriptor.Input("site", "object", required: true), PortDescriptor.Output("address", "object")]),
        new(PlanCalls, "plan calls",
            [PortDescriptor.Input("site", "object", required: true), PortDescriptor.Input("address", "object"), PortDescriptor.Output("calls", "object")]),
        new(RecordResults, "record results",
        [
            PortDescriptor.Input("resolved", "object"), PortDescriptor.Input("unresolved", "object"),
            PortDescriptor.Output("resolved", "object"), PortDescriptor.Output("unresolved", "object"),
        ]),
        new(ResolveWords, "resolve words",
            [PortDescriptor.Input("resolved", "object", required: true), PortDescriptor.Output("written", "object"), PortDescriptor.Output("unknownWords", "object")]),
        new(StartAnalysis, "start analysis",
            [PortDescriptor.Input("site", "object", required: true), PortDescriptor.Output("started", "object"), PortDescriptor.Output("reason", "string")]),
        new(OfferSource, "offer a source", [PortDescriptor.Input("source", "object", required: true), PortDescriptor.Output("outstanding", "object")]),
    ];

    private readonly CoveringSourceService _coveringSources;
    private readonly CoverageLedger _ledger;
    private readonly DivisionResolver _divisions;
    private readonly VocabularyEdgeWriter _vocabularyEdges;
    private readonly AnalysisSpawner _analysis;
    private readonly int _daysPerSlice;

    public ForageSteps(
        IHttpClientFactory httpClientFactory,
        ILogger<ForageSteps> logger,
        string myceliumUrl,
        LaunchToken? launchToken,
        string? apiKey,
        CoveringSourceService coveringSources,
        CoverageLedger ledger,
        DivisionResolver divisions,
        VocabularyEdgeWriter vocabularyEdges,
        AnalysisSpawner analysis,
        int daysPerSlice)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey)
    {
        _coveringSources = coveringSources;
        _ledger = ledger;
        _divisions = divisions;
        _vocabularyEdges = vocabularyEdges;
        _analysis = analysis;
        _daysPerSlice = daysPerSlice;
    }

    // Every port any step declares. A box is declared from one step's manifest, which ManifestOf answers.
    public override IReadOnlyList<PortDescriptor> Ports { get; } = Kinds.SelectMany(kind => kind.Ports).Distinct().ToList();

    public static object? ManifestOf(string step) => Kinds.FirstOrDefault(kind => kind.Name == step) is { } kind
        ? new
        {
            service = $"Forage {kind.Title}",
            ports = kind.Ports,
            settings = new[] { new { name = StepSetting, required = true, values = new[] { kind.Name }, @default = kind.Name } },
        }
        : null;

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken) =>
        (context.Param(StepSetting) is { ValueKind: JsonValueKind.String } step ? step.GetString() : null) switch
        {
            ResolveRegion => ResolveRegionAsync(context, cancellationToken),
            PlanCalls => PlanCallsAsync(context, cancellationToken),
            RecordResults => RecordResultsAsync(context, cancellationToken),
            ResolveWords => ResolveWordsAsync(context, cancellationToken),
            StartAnalysis => StartAnalysisAsync(context, cancellationToken),
            OfferSource => OfferSourceAsync(context, cancellationToken),
            _ => throw new InvalidOperationException(
                $"A Forage box names its step in the '{StepSetting}' setting, one of: {string.Join(", ", Kinds.Select(kind => kind.Name))}."),
        };

    private async Task<NodeResult> ResolveRegionAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var siteId = ThingIdOn(context, "site");
        var coverage = await SiteCoverageAsync(siteId, cancellationToken);
        var address = new Dictionary<string, string>(coverage.Address, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in await _divisions.RegionAsync(siteId, coverage, cancellationToken) ?? new Dictionary<string, string>())
            address[name] = value;
        return NodeResult.Ok(("address", (object?)Json(address)));
    }

    private async Task<NodeResult> PlanCallsAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var siteId = ThingIdOn(context, "site");
        var coverage = await SiteCoverageAsync(siteId, cancellationToken);
        if (TextValues(context.Input("address")) is { } address)
            coverage = coverage with { Covering = CoveringSourceResolver.AlsoAddressedWith(coverage.Covering, address) };

        foreach (var source in coverage.Covering.Where(source => source.Calls.Count == 0))
            Logger.LogInformation("Source {Source} resolves onto Things site {SiteId} holds none of; nothing to fetch", source.Name, siteId);
        if (coverage.Vocabulary == null)
            Logger.LogError("The model declares no coverage archetype, so nothing is recorded about what site {SiteId}'s calls find, "
                + "and every call will be planned again", siteId);

        var outstanding = await _ledger.OutstandingAsync(coverage, cancellationToken);
        return NodeResult.Ok(("calls", (object?)Json(PlannedCalls.From(outstanding, _daysPerSlice))));
    }

    private async Task<NodeResult> RecordResultsAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var outcomes = ListOn(context, "resolved").Select(outcome => PlannedCalls.OutcomeOf(outcome, resolved: true))
            .Concat(ListOn(context, "unresolved").Select(outcome => PlannedCalls.OutcomeOf(outcome, resolved: false)));

        var results = new List<CallResult>();
        foreach (var (call, result) in PlannedCalls.Fold(outcomes))
        {
            await _ledger.RecordAsync(
                new OutstandingCall(
                    new CoveringSource(call.SourceId, call.Source, call.EndpointName, []),
                    new SourceCall(call.SubjectId, call.Subject, call.AddressParameters),
                    call.RecordId, call.Attempts),
                new SourceOutcome(result.Source, result.Resolved, result.Reason, result.Subject, result.SubjectId, result.Written, result.SourceId),
                cancellationToken);
            if (!result.Resolved)
                Logger.LogWarning("Source {Source} left {Subject} undiscovered: {Reason}", result.Source, result.Subject, result.Reason);
            results.Add(result);
        }

        return NodeResult.Ok(
            ("resolved", (object?)Json(results.Where(result => result.Resolved))),
            ("unresolved", Json(results.Where(result => !result.Resolved))));
    }

    private async Task<NodeResult> ResolveWordsAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var resolved = ListOn(context, "resolved")
            .Select(result => result.Deserialize<CallResult>(JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("Resolve words was handed a resolved call it cannot read."))
            .Select(result => new SourceOutcome(result.Source, true, null, result.Subject, result.SubjectId, result.Written, result.SourceId))
            .ToList();

        var words = await _vocabularyEdges.ResolveAsync(new DiscoveryReport(resolved, []), cancellationToken)
            ?? throw new FailureCanBeTriedAgainException("Could not read the vocabulary declarations the fetched words resolve by.");

        var outputs = new Dictionary<string, object?>
        {
            ["written"] = Json(words.Written.Select(Relationship)),
            ["unknownWords"] = Json(words.Unknown),
        };
        if (words.NotWritten.Count > 0)
            throw new FailureCanBeTriedAgainException(
                "Could not write the relationships for: " + string.Join(", ", words.NotWritten.Select(edge => $"'{edge.Word}' on {edge.SubjectName}")) + ".",
                outputs: outputs);
        return NodeResult.Ok(outputs);
    }

    private async Task<NodeResult> StartAnalysisAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var siteId = ThingIdOn(context, "site");
        var coverage = await SiteCoverageAsync(siteId, cancellationToken);
        if (!await _ledger.StampWorkedOutAsync(siteId, cancellationToken))
            throw new FailureCanBeTriedAgainException($"Could not stamp site {siteId} as worked out.");

        var spawn = await _analysis.SpawnAsync(siteId, coverage.Analysis, cancellationToken);
        var outputs = new Dictionary<string, object?> { ["started"] = Json(spawn.Running), ["reason"] = spawn.Reason };
        if (spawn.Refused)
            throw new FailureCanBeTriedAgainException(spawn.Reason!, outputs: outputs);
        return NodeResult.Ok(outputs);
    }

    private async Task<NodeResult> OfferSourceAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var sourceId = ThingIdOn(context, "source");
        var coverage = await _coveringSources.ForSourceAsync(sourceId, cancellationToken)
            ?? throw new FailureCanBeTriedAgainException($"Could not read which sites source {sourceId} reaches.");
        if (coverage.Vocabulary == null)
            throw new InvalidOperationException(
                $"The model declares no coverage archetype, so source {sourceId} cannot be offered to the sites it covers.");

        var outstanding = await _ledger.OutstandingAsync(coverage, cancellationToken);
        if (!await _ledger.StampWorkedOutAsync(sourceId, cancellationToken))
            throw new FailureCanBeTriedAgainException($"Could not stamp source {sourceId} as offered.");

        return NodeResult.Ok(("outstanding", (object?)Json(outstanding.Select(call => new
        {
            subjectId = call.Call.SubjectId,
            subject = call.Call.SubjectName,
            sourceId = call.Source.SourceId,
            source = call.Source.Name,
            recordId = call.CoverageId,
        }))));
    }

    private async Task<SiteCoverage> SiteCoverageAsync(Guid siteId, CancellationToken cancellationToken) =>
        await _coveringSources.ForSiteAsync(siteId, cancellationToken)
        ?? throw new FailureCanBeTriedAgainException($"Could not read which sources cover site {siteId}.");

    private static object Relationship(PlannedVocabularyEdge edge) => new
    {
        subjectId = edge.SubjectId,
        subject = edge.SubjectName,
        word = edge.Word,
        memberId = edge.MemberId,
    };

    // A Thing reaches a port as its id, or as the object a run's subject is, naming it by "id".
    private static Guid ThingIdOn(NodeContext context, string port)
    {
        var value = context.Input(port);
        var id = value is { ValueKind: JsonValueKind.Object } thing && thing.TryGetProperty("id", out var named) ? named : value;
        return id is { ValueKind: JsonValueKind.String } text && text.TryGetGuid(out var thingId)
            ? thingId
            : throw new InvalidOperationException($"A Forage step takes the {port} on its '{port}' input as a Thing: its id, or an object naming it by \"id\".");
    }

    // Absent and null are an empty list: a fail wire that carried nothing still settles the port.
    private static IEnumerable<JsonElement> ListOn(NodeContext context, string port) => context.Input(port) switch
    {
        { ValueKind: JsonValueKind.Array } list => list.EnumerateArray().Where(item => item.ValueKind != JsonValueKind.Null).ToList(),
        null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => [],
        _ => throw new InvalidOperationException($"A Forage step takes a list on its '{port}' input."),
    };

    private static IReadOnlyDictionary<string, string>? TextValues(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Object } values
            ? values.EnumerateObject()
                .Where(entry => entry.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!, StringComparer.OrdinalIgnoreCase)
            : null;

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);
}
