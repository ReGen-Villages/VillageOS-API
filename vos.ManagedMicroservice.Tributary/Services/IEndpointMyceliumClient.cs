namespace vos.ManagedMicroservice.Tributary.Services;

public interface IEndpointMyceliumClient
{
    Task<MyceliumClient.MyceliumThing?> FindThingByNameAsync(string name);
    Task<MyceliumClient.MyceliumThing?> CreateThingAsync(string name, Dictionary<string, object?>? properties = null);
    Task<bool> CreateRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId);
}
