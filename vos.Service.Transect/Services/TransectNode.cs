using System.Net;
using System.Text;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Geometry;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Transect.Services;

// Measures with the formula the intake measures a submission with, so a parcel measured again reads the figure a
// new submission would.
public sealed class TransectNode : DagNodeService
{
    public const string SubjectPort = "subject";
    public const string BoundaryProperty = "boundary";
    public const string MeasuredAreaProperty = "measuredAreaHectares";

    private const string NoSubject =
        "Transect measures the Parcel on its 'subject' input: its identifier, or an object naming it by \"id\".";

    private static readonly TimeSpan BrokerWait = TimeSpan.FromSeconds(30);

    private readonly SubscriptionClient _snapshots;

    public TransectNode(
        IHttpClientFactory httpClientFactory, ILogger<TransectNode> logger, string myceliumUrl,
        LaunchToken? launchToken = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, apiKey) =>
        _snapshots = new SubscriptionClient(httpClientFactory, logger, myceliumUrl, launchToken, apiKey: apiKey);

    public override IReadOnlyList<PortDescriptor> Ports { get; } =
    [
        PortDescriptor.Input(SubjectPort, "object", required: true),
        PortDescriptor.Output(SubjectPort, "object"),
    ];

    protected override async Task<NodeResult> ExecuteNodeAsync(NodeContext context, CancellationToken cancellationToken)
    {
        var subject = context.Input(SubjectPort);
        var parcelId = ParcelOf(subject) ?? throw new NodeFailedException(NoSubject);

        var snapshot = await ReadAsync(parcelId, cancellationToken);
        AreaMethodChoice method;
        try
        {
            method = AreaMethodReader.Read(snapshot);
        }
        catch (AreaMethodNotDeclaredException refused)
        {
            throw new NodeFailedException(refused.Message);
        }

        var parcel = snapshot.Things.FirstOrDefault(thing => thing.Id == parcelId)
            ?? throw new NodeFailedException($"The model holds no Parcel {parcelId}, so there is no border to measure.");
        var hectares = BorderArea.Hectares(Corners(parcel), method.Formula);

        var answer = await AskToApplyFragmentAsync(Fragment(parcel, hectares, method), BrokerWait, cancellationToken);
        if (!answer.IsDone)
            throw (int)answer.Status >= 500
                ? new FailureCanBeTriedAgainException($"The model could not take the measure: {answer.Body}")
                : new NodeFailedException($"The model refused the measure: {answer.Body}");

        return NodeResult.Ok(new Dictionary<string, object?> { [SubjectPort] = subject });
    }

    private async Task<SnapshotDocument> ReadAsync(Guid parcelId, CancellationToken cancellationToken)
    {
        try
        {
            return (await _snapshots.ReadSnapshotAsync(new SubscriptionSelector
            {
                Ids = [parcelId, .. RoleBindings.Identifiers(AreaMethodReader.Roles)],
                Names = [RoleBindings.BindsPredicateName],
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

    private static Guid? ParcelOf(JsonElement? subject)
    {
        var named = subject is { ValueKind: JsonValueKind.Object } thing && thing.TryGetProperty("id", out var id) ? id : subject;
        return named is { ValueKind: JsonValueKind.String } text && Guid.TryParse(text.GetString(), out var parsed) ? parsed : null;
    }

    // A GeoJSON value may arrive as the polygon itself or as its text, depending on how it was written.
    private static IReadOnlyList<(double Latitude, double Longitude)> Corners(SnapshotThing parcel)
    {
        if (!parcel.Properties.TryGetValue(BoundaryProperty, out var stated) || stated.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new NodeFailedException($"'{parcel.Name}' has no {BoundaryProperty}, so there is no border to measure.");

        JsonElement polygon;
        try
        {
            polygon = stated.Value.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(stated.Value.GetString()!).RootElement
                : stated.Value;
        }
        catch (JsonException)
        {
            throw new NodeFailedException($"'{parcel.Name}' holds a {BoundaryProperty} that is not GeoJSON.");
        }

        if (!polygon.TryGetProperty("type", out var type) || type.GetString() != "Polygon"
            || !polygon.TryGetProperty("coordinates", out var rings) || rings.ValueKind != JsonValueKind.Array || rings.GetArrayLength() == 0)
            throw new NodeFailedException($"'{parcel.Name}' holds a {BoundaryProperty} that is not a polygon.");

        var corners = rings[0].EnumerateArray()
            .Select(position => (Latitude: position[1].GetDouble(), Longitude: position[0].GetDouble()))
            .ToList();
        if (corners.Count > 1 && corners[0] == corners[^1]) corners.RemoveAt(corners.Count - 1);
        if (corners.Count < 3)
            throw new NodeFailedException($"'{parcel.Name}' has a border of {corners.Count} corner(s), and an area needs at least three.");
        return corners;
    }

    // The predicates are named only by identifier, never listed as Things, so nothing restates a role-bound predicate.
    private static StringContent Fragment(SnapshotThing parcel, double hectares, AreaMethodChoice method)
    {
        var predicate = method.AreaMeasuredWith;
        object Edge(AreaMethodThing target) => new
        {
            Name = $"{parcel.Name} {predicate.Name} {target.Name}",
            Subject = parcel.Id,
            Predicate = predicate.Id,
            Target = target.Id,
        };

        var fragment = new
        {
            Name = $"{parcel.Name} measured with {method.Method.Name}",
            Things = new[]
            {
                new
                {
                    parcel.Id,
                    Name = parcel.Name ?? parcel.Id.ToString(),
                    Properties = new Dictionary<string, object>
                    {
                        [MeasuredAreaProperty] = new { typeInfo = "vos.Double", value = hectares },
                    },
                },
            },
            Relationships = new[] { Edge(method.Method) },
            Retractions = method.OtherMethods.Select(Edge).ToArray(),
        };
        return new StringContent(JsonSerializer.Serialize(fragment), Encoding.UTF8, "application/json");
    }
}
