namespace vos.Service.Shared.Contracts.Validation;

// Marks a DTO with the $id of the JSON Schema that pins its wire format.
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
