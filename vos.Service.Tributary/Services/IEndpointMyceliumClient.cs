using vos.Service.Shared;

namespace vos.Service.Tributary.Services;

public interface IEndpointMyceliumClient
{
    Task<MyceliumClient.MyceliumThing?> FindThingByNameAsync(string name);
    Task<MyceliumClient.MyceliumThing?> CreateThingAsync(string name, Dictionary<string, object?>? properties = null);
    Task<bool> CreateRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId);

    Task<bool> SubmitObservationsAsync(Guid thingId, IReadOnlyList<ObservationSample> samples);

    Task<bool> SetPropertyModeAsync(Guid thingId, string property, string mode);
}
