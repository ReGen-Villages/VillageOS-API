namespace vos.Microservice.Shared.Contracts.Validation;

/// <summary>
/// Outcome of a <see cref="SchemaValidator"/> call. Immutable; safe to pass across logging boundaries.
/// </summary>
public sealed record ContractValidationResult(bool IsValid, IReadOnlyList<ContractValidationError> Errors)
{
    public static ContractValidationResult Success { get; } =
        new(true, Array.Empty<ContractValidationError>());

    public static ContractValidationResult Failure(IReadOnlyList<ContractValidationError> errors) =>
        new(false, errors);
}

/// <summary>
/// A single schema violation. <see cref="Path"/> is a JSON Pointer (RFC 6901) into the payload
/// (null only if the underlying validator did not populate it); <see cref="Code"/> mirrors the
/// public code family (e.g. "Required", "AdditionalProperties", "Format", "Type", "ArrayLength").
/// </summary>
public sealed record ContractValidationError(string? Path, string Code, string Message);
