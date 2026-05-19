namespace vos.ManagedMicroservice.Tributary.Services;

public interface IEndpointBrokerClient
{
    Task<BrokerClient.BrokerThing?> FindThingByNameAsync(string name);
    Task<BrokerClient.BrokerThing?> CreateThingAsync(string name, Dictionary<string, object?>? properties = null);
    Task<bool> CreateRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId);
}
