using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Stomata.Services;

public sealed class StomataNode : DagNodeService
{
    public const string SubjectPort = "subject";
    public const string StampProperty = "climateWorkedOutAt";
    public const string WindRoseCellReading = "windRoseCell";
    public const string HumidityRatioReading = "humidityRatioGramsPerKilogram";
    public const string WindowsOpenReading = "windowsOpenHours";
    public const string ElevationProperty = "climateDataElevationMetres";
    public const string ParticulateMatterTenLimit = "whoDailyLimitParticulateMatterTenMicrogramsPerCubicMetre";
    public const string ParticulateMatterTwoAndAHalfLimit = "whoDailyLimitParticulateMatterTwoAndAHalfMicrogramsPerCubicMetre";
    public const string TemperatureSeries = "temperatureCelsius";
    public const string RelativeHumiditySeries = "relativeHumidityPercent";
    public const string WindSpeedSeries = "windSpeedMetresPerSecond";
    public const string WindDirectionSeries = "windDirectionDegrees";
    public const string ParticulateMatterTenSeries = "particulateMatterTenMicrogramsPerCubicMetre";
    public const string ParticulateMatterTwoAndAHalfSeries = "particulateMatterTwoAndAHalfMicrogramsPerCubicMetre";

    private const string ServiceName = "Stomata";

    private const string NoSubject =
        "Stomata works out the SiteStudy on its 'subject' input: its identifier, or an object naming it by \"id\".";

    private readonly SubscriptionClient _snapshots;
    private readonly TimeProvider _clock;

    public StomataNode(
        IHttpClientFactory httpClientFactory, ILogger<StomataNode> logger, string myceliumUrl,
        LaunchToken? launchToken = null, string? apiKey = null, TimeProvider? clock = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey)
    {
        _snapshots = new SubscriptionClient(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey);
        _clock = clock ?? TimeProvider.System;
    }

    public override IReadOnlyList<PortDescriptor> Ports { get; } =
    [
        PortDescriptor.Input(SubjectPort, "object", required: true),
        PortDescriptor.Output(SubjectPort, "object"),
    ];

    protected override async Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var subject = context.Input(SubjectPort);
        var studyId = StudyOf(subject) ?? throw new NodeFailedException(NoSubject);

        var snapshot = await ReadAsync(studyId, cancellationToken);
        var study = snapshot.Things.FirstOrDefault(thing => thing.Id == studyId)
            ?? throw new NodeFailedException($"The model holds no study {studyId}, so there is no climate to work out.");

        if (SiteOf(snapshot, studyId) is { } siteId)
        {
            var readings = await WorkOutAsync(studyId, siteId, WindRose.From(snapshot), cancellationToken);
            if (readings.Count > 0)
            {
                Accept(await DepositReadingsAsync(siteId, readings, cancellationToken), "the hourly readings");
                Accept(await ReportHistoryChangedAsync(siteId, cancellationToken), "the change to the site's history");
            }
        }

        await StampAsync(study, cancellationToken);
        return NodeResult.Ok(new Dictionary<string, object?> { [SubjectPort] = subject });
    }

    private async Task<List<ObservationSample>> WorkOutAsync(Guid studyId, Guid siteId, WindRose rose, CancellationToken cancellationToken)
    {
        var studyInputs = new StudyInputs(await GetAsync(MyceliumRoutes.ThingProperties(studyId), cancellationToken) ?? Empty, ServiceName);
        var siteInputs = new StudyInputs(await GetAsync(MyceliumRoutes.ThingProperties(siteId), cancellationToken) ?? Empty, ServiceName);

        var hours = HourlyClimate.Join(
            await SeriesAsync(siteId, TemperatureSeries, cancellationToken),
            await SeriesAsync(siteId, RelativeHumiditySeries, cancellationToken),
            await SeriesAsync(siteId, WindSpeedSeries, cancellationToken),
            await SeriesAsync(siteId, WindDirectionSeries, cancellationToken),
            await SeriesAsync(siteId, ParticulateMatterTenSeries, cancellationToken),
            await SeriesAsync(siteId, ParticulateMatterTwoAndAHalfSeries, cancellationToken));

        double? elevation = siteInputs.WaitingFor([ElevationProperty]).Count == 0 ? siteInputs.Number(ElevationProperty) : null;
        var windowsOpen = studyInputs.WaitingFor([ParticulateMatterTenLimit, ParticulateMatterTwoAndAHalfLimit]).Count == 0
            ? WindowsOpen.Of(hours, new ParticulateLimits(
                studyInputs.Number(ParticulateMatterTenLimit), studyInputs.Number(ParticulateMatterTwoAndAHalfLimit)))
            : [.. hours.Select(_ => (double?)null)];

        var readings = new List<ObservationSample>();
        foreach (var (hour, open) in hours.Zip(windowsOpen))
        {
            if (rose.CellOf(hour.WindDirectionDegrees, hour.WindSpeedMetresPerSecond) is { } cell)
                readings.Add(new ObservationSample(WindRoseCellReading, cell, hour.At));
            if (hour is { TemperatureCelsius: { } celsius, RelativeHumidityPercent: { } humidity } && elevation is { } metres)
                readings.Add(new ObservationSample(HumidityRatioReading, Psychrometrics.HumidityRatioGramsPerKilogram(celsius, humidity, metres), hour.At));
            if (open is { } windows)
                readings.Add(new ObservationSample(WindowsOpenReading, windows, hour.At));
        }
        return readings;
    }

    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    private async Task<IReadOnlyList<Reading>> SeriesAsync(Guid siteId, string series, CancellationToken cancellationToken)
    {
        var answer = await GetAsync($"/api/things/{siteId}/properties/{Uri.EscapeDataString(series)}/observations", cancellationToken);
        if (answer is not { } observations || !observations.TryGetProperty("samples", out var samples)) return [];
        return
        [
            .. samples.EnumerateArray()
                .Where(sample => sample.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number)
                .Select(sample => new Reading(sample.GetProperty("observedAt").GetDateTime().ToUniversalTime(), sample.GetProperty("value").GetDouble())),
        ];
    }

    // Null where the model holds no such Thing or property, which leaves a series or an input unknown rather than failing the run.
    private async Task<JsonElement?> GetAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateAuthenticatedClientAsync(BrokerPatience);
            using var response = await client.GetAsync($"{MyceliumUrl}{path}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if ((int)response.StatusCode >= 500)
                throw new FailureCanBeTriedAgainException($"The model could not answer {path}: {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
                throw new NodeFailedException($"The model refused {path}: {(int)response.StatusCode}");
            return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).Clone();
        }
        catch (HttpRequestException unreachable)
        {
            throw new FailureCanBeTriedAgainException($"The model could not be reached for {path}: {unreachable.Message}");
        }
    }

    private async Task<SnapshotDocument> ReadAsync(Guid studyId, CancellationToken cancellationToken)
    {
        try
        {
            return (await _snapshots.ReadSnapshotAsync(new SubscriptionSelector
            {
                Ids = [studyId, LandIntakeRoles.StudiedSite.Id, .. RoleBindings.Identifiers(StomataRoles.WindRose)],
                Names = [RoleBindings.BindsPredicateName, WindRose.IsPredicateName],
                BoundTypes = RoleBindings.Identifiers(StomataRoles.WindRose),
                Traverse = [RoleBindings.FollowBindings],
                IncludeRelationships = true,
            }, cancellationToken)).Snapshot;
        }
        catch (HttpRequestException unreachable) when (unreachable.StatusCode is null or >= HttpStatusCode.InternalServerError)
        {
            throw new FailureCanBeTriedAgainException($"The model could not be read: {unreachable.Message}");
        }
        catch (HttpRequestException refused)
        {
            throw new NodeFailedException($"The model refused the read: {refused.Message}");
        }
    }

    private static Guid? SiteOf(SnapshotDocument snapshot, Guid studyId)
    {
        if (snapshot.Bound(LandIntakeRoles.StudiedSite) is not [var studies]) return null;
        return snapshot.Relationships
            .Where(edge => edge.SubjectId == studyId && edge.PredicateId == studies.Id)
            .Select(edge => (Guid?)edge.TargetId)
            .FirstOrDefault();
    }

    private static void Accept(BrokerAnswer answer, string what)
    {
        if (answer.Accepted) return;
        if (answer.CanBeTriedAgain)
            throw new FailureCanBeTriedAgainException($"The model could not take {what} now: {answer.Refusal}", answer.Wait);
        throw new NodeFailedException($"The model refused {what}: {answer.Refusal}");
    }

    private async Task StampAsync(SnapshotThing study, CancellationToken cancellationToken)
    {
        var fragment = new
        {
            Name = $"{study.Name} climate worked out",
            Things = new[]
            {
                new
                {
                    study.Id,
                    Name = study.Name ?? study.Id.ToString(),
                    Properties = new Dictionary<string, object>
                    {
                        [StampProperty] = new { typeInfo = "vos.DateTime", value = _clock.GetUtcNow().UtcDateTime },
                    },
                },
            },
        };
        var answer = await AskToApplyFragmentAsync(
            new StringContent(JsonSerializer.Serialize(fragment), Encoding.UTF8, "application/json"), BrokerPatience, cancellationToken);
        if (!answer.IsDone)
            throw (int)answer.Status >= 500
                ? new FailureCanBeTriedAgainException($"The model could not take the stamp: {answer.Body}")
                : new NodeFailedException($"The model refused the stamp: {answer.Body}");
    }

    private static Guid? StudyOf(JsonElement? subject)
    {
        var named = subject is { ValueKind: JsonValueKind.Object } thing && thing.TryGetProperty("id", out var id) ? id : subject;
        return named is { ValueKind: JsonValueKind.String } text && Guid.TryParse(text.GetString(), out var parsed) ? parsed : null;
    }
}
