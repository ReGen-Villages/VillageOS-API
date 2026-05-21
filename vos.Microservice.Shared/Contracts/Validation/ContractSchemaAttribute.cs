namespace vos.Microservice.Shared.Contracts.Validation;

/// <summary>
/// Marks a DTO with the <c>$id</c> of the JSON Schema that pins its wire format.
/// Used by <see cref="SchemaRegistry.Get{T}"/> to resolve a schema by type instead of by string.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ContractSchemaAttribute : Attribute
{
    public string Id { get; }

    public ContractSchemaAttribute(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Schema $id must be non-empty.", nameof(id));
        Id = id;
    }
}
