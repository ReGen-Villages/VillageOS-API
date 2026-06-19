namespace vos.ManagedMicroservice.Tributary.Services;

/// <summary>One sampled measurement for a property of an entity, at a source-supplied time.</summary>
public readonly record struct ObservationSample(string Property, object? Value, DateTime ObservedAt);

public interface IEndpointMyceliumClient
{
    Task<MyceliumClient.MyceliumThing?> FindThingByNameAsync(string name);
    Task<MyceliumClient.MyceliumThing?> CreateThingAsync(string name, Dictionary<string, object?>? properties = null);
    Task<bool> CreateRelationshipAsync(Guid subjectId, Guid predicateId, Guid targetId);

    /// <summary>Submit many samples across one entity's properties in a single batch (sediment ingest).</summary>
    Task<bool> SubmitObservationsAsync(Guid thingId, IReadOnlyList<ObservationSample> samples);

    /// <summary>Bound an observed property's retention to the given PropertyMode (e.g. Sampled).</summary>
    Task<bool> SetPropertyModeAsync(Guid thingId, string property, string mode);
}
