namespace vos.ManagedMicroservice.Shared.Contracts.Validation;

/// <summary>
/// Failure policy for contract-validation failures. Picked by the call site
/// (typically <c>MyceliumClientBase</c>) so the same validator can be used in
/// both fail-fast (dev) and fail-soft (prod) contexts.
/// </summary>
public enum SchemaViolationMode
{
    /// <summary>Throw <see cref="ContractValidationException"/> on the first failure.</summary>
    Throw,

    /// <summary>Log a single warning per failed call; do not throw.</summary>
    Log
}
