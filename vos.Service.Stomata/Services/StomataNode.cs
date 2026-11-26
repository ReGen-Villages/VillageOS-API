using vos.Service.Shared;
using vos.Service.Shared.DagNode;

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

    private readonly TimeProvider _clock;

    public StomataNode(
        IHttpClientFactory httpClientFactory, ILogger<StomataNode> logger, string myceliumUrl,
        LaunchToken? launchToken = null, string? apiKey = null, TimeProvider? clock = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        _clock = clock ?? TimeProvider.System;

    public override IReadOnlyList<PortDescriptor> Ports { get; } =
    [
        PortDescriptor.Input(SubjectPort, "object", required: true),
        PortDescriptor.Output(SubjectPort, "object"),
    ];

    protected override Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken) =>
        Task.FromResult(NodeResult.Ok(new Dictionary<string, object?> { [SubjectPort] = context.Input(SubjectPort) }));
}
