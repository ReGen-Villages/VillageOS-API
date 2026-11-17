using vos.Service.Shared;

namespace vos.Service.Tributary.Services;

public interface IEndpointMyceliumClient
{
    Task<MyceliumClient.MyceliumThing?> FindThingByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<MyceliumClient.MyceliumThing?> CreateThingAsync(
        string name, Dictionary<string, object?>? properties, CancellationToken cancellationToken = default);

    Task<bool> CreateRelationshipAsync(
        Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken = default);

    Task<bool> ApplyFragmentAsync(object fragment, CancellationToken cancellationToken = default);

    Task<bool> SubmitObservationsAsync(
        Guid thingId, IReadOnlyList<ObservationSample> samples, CancellationToken cancellationToken = default);

    Task<bool> SetPropertyModeAsync(
        Guid thingId, string property, string mode, int? sampleSeconds = null, CancellationToken cancellationToken = default);
}
