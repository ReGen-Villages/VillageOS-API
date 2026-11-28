using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Subscriptions;
using vos.Service.Watershed.SiteLayers;
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
    private const string GeometryProperty = "geometry";
    private const string HectaresInside = "hectaresInside";
    private const string MetresInside = "metresInside";

    private static readonly (string Grid, long ReturnPeriodYears, bool BuiltOver)[] RunoffGrids =
    [
        ("runoffClassOneInFiveGrid", 5, false),
        ("runoffClassOneInHundredGrid", 100, false),
        ("runoffClassOneInHundredBuiltOverGrid", 100, true),
    ];

    private const string NoSubject =
        "Watershed works out the ground of the SiteStudy on its 'subject' input: its identifier, or an object naming it by \"id\".";

    private static readonly TimeSpan BrokerWait = TimeSpan.FromSeconds(30);

    private const string ModelReadPart = "the model read";
    private const string HeightCallsPart = "the height calls";
    private const string LandCoverReadPart = "the land cover read";
    private const string ObservationWritesPart = "the observation writes";
    private const string ReadBackPart = "the read back";
    private const string ReadBackPausesPart = "the pauses between read backs";
    private const string StampPart = "the stamp";

    private readonly TimeProvider _clock;
    private readonly GroundHeightReader _heights;
    private readonly LandCoverReader _landCover;
    private readonly SubscriptionClient _snapshots;

    public WatershedNode(
        IHttpClientFactory httpClientFactory, ILogger<WatershedNode> logger, string myceliumUrl, GroundHeightReader heights,
        LandCoverReader landCover, TimeProvider clock, LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey)
    {
        _heights = heights;
        _landCover = landCover;
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

        var times = new GroundStepTimes(_clock);
        try
        {
            var ground = GroundReading.Read(
                await times.TimeAsync(ModelReadPart, () => ReadAsync(GroundReading.Selector(studyId), cancellationToken)), studyId);
            if (ground.Border is { } border)
            {
                List<(Guid Thing, string Property, double Value)> written = [];
                if (ground.HeightsSource is { } source)
                    written.AddRange(await WorkOutAsync(ground, border, source, times, cancellationToken));
                if (ground.LandCoverTileAddress is { } tileAddress && ground.LandCoverClasses.Count > 0)
                    written.AddRange(await CoverLandAsync(ground, border, tileAddress, times, cancellationToken));
                written.AddRange(await CoverSiteLayersAsync(ground.LayerFeatures, border, times, cancellationToken));
                if (written.Count > 0)
                    await ReadBackAsync(written, times, cancellationToken);
            }

            await StampAsync(ground.Study, times, cancellationToken);
            return NodeResult.Ok(new Dictionary<string, object?> { [SubjectPort] = subject });
        }
        finally
        {
            LogTimes(context, times);
        }
    }

    private void LogTimes(NodeContext context, GroundStepTimes times)
    {
        foreach (var part in times.PartsWhenStopped())
            if (part.UnderWay)
                Logger.LogInformation(
                    "Ground step {NodeId} (run {RunId}): {Part} took {Milliseconds} ms over {Calls} finished calls and was under way when the step stopped",
                    context.NodeId, context.RunId, part.Name, Milliseconds(part.Taken), part.Calls);
            else
                Logger.LogInformation("Ground step {NodeId} (run {RunId}): {Part} took {Milliseconds} ms over {Calls} calls",
                    context.NodeId, context.RunId, part.Name, Milliseconds(part.Taken), part.Calls);
        Logger.LogInformation("Ground step {NodeId} (run {RunId}) took {Milliseconds} ms in all",
            context.NodeId, context.RunId, Milliseconds(times.Total));
    }

    private static long Milliseconds(TimeSpan taken) => (long)Math.Round(taken.TotalMilliseconds);

    private async Task<IReadOnlyList<(Guid Thing, string Property, double Value)>> WorkOutAsync(
        GroundReading ground, IReadOnlyList<Position> border, string source, GroundStepTimes times, CancellationToken cancellationToken)
    {
        var layout = HeightGridLayout.Over(border);
        var (westToEast, southToNorth) = ProfileLines.Through(border);

        var heights = StoredGrid.Read(ground.Site!, HeightGrid) is { } stored && stored.LaidOutAs(layout)
            ? stored.Values
            : await times.TimeAsync(HeightCallsPart, () => _heights.HeightsAtAsync(source, layout.PointsRowByRowFromTheSouthWest, cancellationToken));
        IReadOnlyList<double> profileHeights = StoredGrid.Read(ground.Site!, WestToEastProfile) is { } storedWestToEast && storedWestToEast.LaidOutAs(westToEast, 1)
                             && StoredGrid.Read(ground.Site!, SouthToNorthProfile) is { } storedSouthToNorth && storedSouthToNorth.LaidOutAs(southToNorth, ProfileLines.PointsAlongALine)
            ? [.. storedWestToEast.Values, .. storedSouthToNorth.Values]
            : await times.TimeAsync(HeightCallsPart, () => _heights.HeightsAtAsync(source, [.. westToEast.Points, .. southToNorth.Points], cancellationToken));

        var surface = new TerrainSurface(layout.Columns, layout.Rows, layout.CellSizeMetres, heights, layout.Inside);
        var analysed = TerrainAnalysis.Of(surface);
        var figures = analysed.Figures;
        var lowest = layout.PointsRowByRowFromTheSouthWest[figures.LowestCell];
        var runoff = RunoffReading.Read(
            await times.TimeAsync(ModelReadPart, () => ReadAsync(RunoffReading.Selector(ground.Site!.Id), cancellationToken)),
            ground.Site!.Id, border.Average(corner => corner.Longitude));

        object Grid(IReadOnlyList<double> values) => StoredGrid.Written(layout, values);
        var siteValues = new Dictionary<string, object>
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
        };
        if (runoff is not null && RunoffInputsOf(ground.Study) is { } inputs)
        {
            var hollows = Hollows.Of(surface, analysed);
            foreach (var (grid, returnPeriodYears, builtOver) in RunoffGrids)
                if (runoff.StormsByReturnPeriodYears.TryGetValue(returnPeriodYears, out var storm))
                    siteValues[grid] = Grid(StormRunoff.GridCodesOf(surface, analysed, hollows, inputs, storm, builtOver, runoff.Classes));
        }
        await RecordAsync(ground.Site!.Id, siteValues, times, cancellationToken);
        List<(Guid Thing, string Property, double Value)> written = [(ground.Site!.Id, MeanHeight, figures.MeanMetres)];

        foreach (var share in GroundShares.InSlopeClasses(analysed, ground.SlopeClasses))
        {
            await RecordAsync(ground.ShareAssessing[share.Term], new Dictionary<string, object>
            {
                [SharePercent] = share.SharePercent, ["areaHectares"] = share.AreaHectares,
            }, times, cancellationToken);
            written.Add((ground.ShareAssessing[share.Term], SharePercent, share.SharePercent));
        }
        foreach (var share in GroundShares.FacingAspects(analysed, ground.Aspects))
        {
            await RecordAsync(ground.ShareAssessing[share.Term], new Dictionary<string, object> { [SharePercent] = share.SharePercent }, times, cancellationToken);
            written.Add((ground.ShareAssessing[share.Term], SharePercent, share.SharePercent));
        }
        return written;
    }

    private async Task<IReadOnlyList<(Guid Thing, string Property, double Value)>> CoverLandAsync(
        GroundReading ground, IReadOnlyList<Position> border, string tileAddress, GroundStepTimes times, CancellationToken cancellationToken)
    {
        List<(Guid Thing, string Property, double Value)> written = [];
        foreach (var share in await times.TimeAsync(LandCoverReadPart, () => _landCover.SharesAsync(tileAddress, border, ground.LandCoverClasses, cancellationToken)))
        {
            var shareThing = ground.ShareAssessing[share.Term];
            await RecordAsync(shareThing, new Dictionary<string, object>
            {
                [SharePercent] = share.SharePercent, ["areaHectares"] = share.AreaHectares, ["pixelCount"] = share.PixelCount,
            }, times, cancellationToken);
            written.Add((shareThing, SharePercent, share.SharePercent));
        }
        return written;
    }

    private async Task<IReadOnlyList<(Guid Thing, string Property, double Value)>> CoverSiteLayersAsync(
        IReadOnlyList<SnapshotThing> features, IReadOnlyList<Position> border, GroundStepTimes times, CancellationToken cancellationToken)
    {
        var frame = LocalFrame.Around(border);
        var site = new PlaneShape([new PlanePolygon([.. border.Select(frame.At)], [])]);
        List<(Guid Thing, string Property, double Value)> written = [];
        if (site.AreaSquareMetres <= 0) return written;

        foreach (var feature in features)
            switch (feature.StatedValue(GeometryProperty) is { } stated ? LayerGeometry.Read(stated.Value, frame) : null)
            {
                case LayerShape { Shape: var shape }:
                    var shapeCoverage = SiteLayerCoverage.OfShape(site, shape);
                    await RecordAsync(feature.Id, new Dictionary<string, object>
                    {
                        [HectaresInside] = shapeCoverage.HectaresInside,
                        ["shareOfSitePercent"] = shapeCoverage.ShareOfSitePercent,
                        ["distanceFromSiteMetres"] = shapeCoverage.DistanceFromSiteMetres,
                    }, times, cancellationToken);
                    written.Add((feature.Id, HectaresInside, shapeCoverage.HectaresInside));
                    break;
                case LayerLines { Lines: var lines }:
                    var lineCoverage = SiteLayerCoverage.OfLines(site, lines);
                    await RecordAsync(feature.Id, new Dictionary<string, object>
                    {
                        [MetresInside] = lineCoverage.MetresInside,
                        ["crossesBorder"] = lineCoverage.CrossesBorder,
                        ["distanceFromSiteMetres"] = lineCoverage.DistanceFromSiteMetres,
                    }, times, cancellationToken);
                    written.Add((feature.Id, MetresInside, lineCoverage.MetresInside));
                    break;
                default:
                    Logger.LogWarning("Site layer feature {Feature} holds no geometry Watershed can read, so its coverage is not worked out.", feature.Id);
                    break;
            }
        return written;
    }

    private async Task RecordAsync(Guid thing, Dictionary<string, object> values, GroundStepTimes times, CancellationToken cancellationToken)
    {
        try
        {
            await times.TimeAsync(ObservationWritesPart,
                () => RecordObservationsAsync(thing, [.. values.Select(value => new ObservationSample(value.Key, value.Value))]));
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
    private async Task ReadBackAsync(
        IReadOnlyList<(Guid Thing, string Property, double Value)> written, GroundStepTimes times, CancellationToken cancellationToken)
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
            var things = (await times.TimeAsync(ReadBackPart, () => ReadAsync(selector, cancellationToken)))
                .Things.DistinctBy(thing => thing.Id).ToDictionary(thing => thing.Id);
            if (written.All(value => things.TryGetValue(value.Thing, out var thing) && thing.StatedNumber(value.Property) == value.Value))
                return;
            if (tried >= tries)
                throw new FailureCanBeTriedAgainException(
                    $"The ground's values were accepted but not stored within {ReadBackForAtMost.TotalSeconds} seconds.");
            await times.TimeAsync(ReadBackPausesPart, () => Task.Delay(ReadBackEvery, _clock, cancellationToken));
        }
    }

    private async Task StampAsync(SnapshotThing study, GroundStepTimes times, CancellationToken cancellationToken)
    {
        var now = await times.TimeAsync(StampPart, () => ReadModelTimeAsync(cancellationToken))
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
        var answer = await times.TimeAsync(StampPart, () => AskToApplyFragmentAsync(
            new StringContent(JsonSerializer.Serialize(fragment), Encoding.UTF8, "application/json"), BrokerWait, cancellationToken));
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

    private static RunoffInputs? RunoffInputsOf(SnapshotThing study) =>
        study.StatedNumber("openGroundRunoffCoefficient") is { } open
        && study.StatedNumber("roofAndPavingRunoffCoefficient") is { } roofAndPaving
        && study.StatedNumber("plantedGroundRunoffCoefficient") is { } planted
        && study.StatedNumber("imperviousShareOfBuiltGround") is { } impervious
        && study.StatedNumber("sheetFlowManningRoughness") is { } sheetRoughness
        && study.StatedNumber("channelManningRoughness") is { } channelRoughness
        && study.StatedNumber("shortestTimeOfConcentrationMinutes") is { } shortest
        && study.StatedNumber("sheetFlowLengthMetres") is { } sheetLength
        && study.StatedNumber("smallestOverlandSlope") is { } overlandSlope
        && study.StatedNumber("smallestChannelSlope") is { } channelSlope
            ? new RunoffInputs(open, roofAndPaving, planted, impervious, sheetRoughness, channelRoughness, shortest, sheetLength, overlandSlope, channelSlope)
            : null;

    private static Guid? StudyOf(JsonElement? subject)
    {
        var named = subject is { ValueKind: JsonValueKind.Object } thing && thing.TryGetProperty("id", out var id) ? id : subject;
        return named is { ValueKind: JsonValueKind.String } text && Guid.TryParse(text.GetString(), out var parsed) ? parsed : null;
    }
}
