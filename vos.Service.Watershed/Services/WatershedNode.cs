using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Subscriptions;
using vos.Service.Watershed.Terrain;

namespace vos.Service.Watershed.Services;

public sealed class WatershedNode : DagNodeService
{
    public const string SubjectPort = "subject";
    public const string StampProperty = "groundWorkedOutAt";

    public static readonly TimeSpan ReadBackEvery = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan ReadBackForAtMost = TimeSpan.FromSeconds(15);

    private const string HeightGrid = "terrainHeightGrid";
    private const string WestToEastProfile = "terrainProfileWestToEast";
    private const string SouthToNorthProfile = "terrainProfileSouthToNorth";
    private const string MeanHeight = "terrainMeanMetres";
    private const string SharePercent = "sharePercent";

    private const string NoSubject =
        "Watershed works out the ground of the SiteStudy on its 'subject' input: its identifier, or an object naming it by \"id\".";

    private static readonly TimeSpan BrokerWait = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock;
    private readonly GroundHeightReader _heights;
    private readonly SubscriptionClient _snapshots;

    public WatershedNode(
        IHttpClientFactory httpClientFactory, ILogger<WatershedNode> logger, string myceliumUrl, GroundHeightReader heights,
        TimeProvider clock, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey)
    {
        _heights = heights;
        _clock = clock;
        _snapshots = new SubscriptionClient(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey);
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

        var ground = GroundReading.Read(await ReadAsync(GroundReading.Selector(studyId), cancellationToken), studyId);
        if (ground.Border is { } border && ground.HeightsSource is { } source)
        {
            var written = await WorkOutAsync(ground, border, source, cancellationToken);
            await ReadBackAsync(written, cancellationToken);
        }

        await StampAsync(ground.Study, cancellationToken);
        return NodeResult.Ok(new Dictionary<string, object?> { [SubjectPort] = subject });
    }

    private async Task<IReadOnlyList<(Guid Thing, string Property, double Value)>> WorkOutAsync(
        GroundReading ground, IReadOnlyList<Position> border, string source, CancellationToken cancellationToken)
    {
        var layout = HeightGridLayout.Over(border);
        var (westToEast, southToNorth) = ProfileLines.Through(border);

        var heights = StoredGrid.Read(ground.Site!, HeightGrid) is { } stored && stored.LaidOutAs(layout)
            ? stored.Values
            : await _heights.HeightsAtAsync(source, layout.Points, cancellationToken);
        IReadOnlyList<double> profileHeights = StoredGrid.Read(ground.Site!, WestToEastProfile) is { } storedWestToEast && storedWestToEast.LaidOutAs(westToEast, 1)
                             && StoredGrid.Read(ground.Site!, SouthToNorthProfile) is { } storedSouthToNorth && storedSouthToNorth.LaidOutAs(southToNorth, ProfileLines.PointsAlongALine)
            ? [.. storedWestToEast.Values, .. storedSouthToNorth.Values]
            : await _heights.HeightsAtAsync(source, [.. westToEast.Points, .. southToNorth.Points], cancellationToken);

        var analysed = TerrainAnalysis.Of(new TerrainSurface(layout.Columns, layout.Rows, layout.CellSizeMetres, heights, layout.Inside));
        var figures = analysed.Figures;
        var lowest = layout.Points[figures.LowestCell];

        object Grid(IReadOnlyList<double> values) => StoredGrid.Written(layout, values);
        await RecordAsync(ground.Site!.Id, new Dictionary<string, object>
        {
            [HeightGrid] = Grid(heights),
            ["terrainSlopeGrid"] = Grid(analysed.SlopePercent),
            ["terrainAspectGrid"] = Grid(analysed.AspectDegrees),
            ["drainageAreaGrid"] = Grid(analysed.DrainageAreaHectares),
            ["hollowDepthGrid"] = Grid(analysed.HollowDepthMetres),
            [WestToEastProfile] = StoredGrid.Written(westToEast, 1, [.. profileHeights.Take(ProfileLines.PointsAlongALine)]),
            [SouthToNorthProfile] = StoredGrid.Written(southToNorth, ProfileLines.PointsAlongALine, [.. profileHeights.Skip(ProfileLines.PointsAlongALine)]),
            ["westToEastSpanFromMetres"] = westToEast.SiteFromMetres,
            ["westToEastSpanToMetres"] = westToEast.SiteToMetres,
            ["southToNorthSpanFromMetres"] = southToNorth.SiteFromMetres,
            ["southToNorthSpanToMetres"] = southToNorth.SiteToMetres,
            ["terrainAreaHectares"] = figures.AreaHectares,
            ["terrainLowestMetres"] = figures.LowestMetres,
            ["terrainHighestMetres"] = figures.HighestMetres,
            [MeanHeight] = figures.MeanMetres,
            ["slopeMeanPercent"] = figures.SlopeMeanPercent,
            ["slopeMedianPercent"] = figures.SlopeMedianPercent,
            ["slopeNinetiethPercentilePercent"] = figures.SlopeNinetiethPercentilePercent,
            ["hollowHectares"] = figures.HollowHectares,
            ["lowestPointLatitude"] = lowest.Latitude,
            ["lowestPointLongitude"] = lowest.Longitude,
        }, cancellationToken);
        List<(Guid Thing, string Property, double Value)> written = [(ground.Site!.Id, MeanHeight, figures.MeanMetres)];

        foreach (var share in GroundShares.InSlopeClasses(analysed, ground.SlopeClasses))
        {
            await RecordAsync(ground.ShareAssessing[share.Term], new Dictionary<string, object>
            {
                [SharePercent] = share.SharePercent, ["areaHectares"] = share.AreaHectares,
            }, cancellationToken);
            written.Add((ground.ShareAssessing[share.Term], SharePercent, share.SharePercent));
        }
        foreach (var share in GroundShares.FacingAspects(analysed, ground.Aspects))
        {
            await RecordAsync(ground.ShareAssessing[share.Term], new Dictionary<string, object> { [SharePercent] = share.SharePercent }, cancellationToken);
            written.Add((ground.ShareAssessing[share.Term], SharePercent, share.SharePercent));
        }
        return written;
    }

    private async Task RecordAsync(Guid thing, Dictionary<string, object> values, CancellationToken cancellationToken)
    {
        try
        {
            await RecordObservationsAsync(thing, [.. values.Select(value => new ObservationSample(value.Key, value.Value))]);
        }
        catch (HttpRequestException unreachable) when (unreachable.StatusCode is null or >= HttpStatusCode.InternalServerError)
        {
            throw new FailureCanBeTriedAgainException($"The model did not take the ground's values: {unreachable.Message}");
        }
        catch (HttpRequestException refused)
        {
            throw new NodeFailedException($"The model refused the ground's values: {refused.Message}");
        }
    }

    // A batch is accepted before it is stored, and the stamp must not land before what it vouches for.
    private async Task ReadBackAsync(IReadOnlyList<(Guid Thing, string Property, double Value)> written, CancellationToken cancellationToken)
    {
        var selector = new SubscriptionSelector
        {
            Ids = [.. written.Select(value => value.Thing).Distinct()],
            IncludeIsAncestors = false,
            IncludeRelationships = false,
        };
        var tries = (int)(ReadBackForAtMost / ReadBackEvery);
        for (var tried = 1; ; tried++)
        {
            var things = (await ReadAsync(selector, cancellationToken)).Things.DistinctBy(thing => thing.Id).ToDictionary(thing => thing.Id);
            if (written.All(value => things.TryGetValue(value.Thing, out var thing) && thing.StatedNumber(value.Property) == value.Value))
                return;
            if (tried >= tries)
                throw new FailureCanBeTriedAgainException(
                    $"The ground's values were accepted but not stored within {ReadBackForAtMost.TotalSeconds} seconds.");
            await Task.Delay(ReadBackEvery, _clock, cancellationToken);
        }
    }

    private async Task StampAsync(SnapshotThing study, CancellationToken cancellationToken)
    {
        var now = await ReadModelTimeAsync(cancellationToken)
                  ?? throw new FailureCanBeTriedAgainException("The model's clock could not be read, so the ground is stamped later.");

        var fragment = new
        {
            Name = $"{study.Name ?? study.Id.ToString()} ground worked out",
            Things = new[]
            {
                new
                {
                    study.Id,
                    Name = study.Name ?? study.Id.ToString(),
                    Properties = new Dictionary<string, object>
                    {
                        [StampProperty] = new { typeInfo = "vos.DateTime", value = now.Now.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
                    },
                },
            },
            Relationships = Array.Empty<object>(),
        };
        var answer = await AskToApplyFragmentAsync(
            new StringContent(JsonSerializer.Serialize(fragment), Encoding.UTF8, "application/json"), BrokerWait, cancellationToken);
        if (!answer.IsDone)
            throw (int)answer.Status >= 500
                ? new FailureCanBeTriedAgainException($"The model could not take the stamp: {answer.Body}")
                : new NodeFailedException($"The model refused the stamp: {answer.Body}");
    }

    private async Task<SnapshotDocument> ReadAsync(SubscriptionSelector selector, CancellationToken cancellationToken)
    {
        try
        {
            return (await _snapshots.ReadSnapshotAsync(selector, cancellationToken)).Snapshot;
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

    private static Guid? StudyOf(JsonElement? subject)
    {
        var named = subject is { ValueKind: JsonValueKind.Object } thing && thing.TryGetProperty("id", out var id) ? id : subject;
        return named is { ValueKind: JsonValueKind.String } text && Guid.TryParse(text.GetString(), out var parsed) ? parsed : null;
    }
}
