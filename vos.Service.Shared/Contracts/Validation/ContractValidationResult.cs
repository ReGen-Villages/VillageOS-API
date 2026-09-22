namespace vos.Service.Shared.Contracts.Validation;

public sealed record ContractValidationResult(bool IsValid, IReadOnlyList<ContractValidationError> Errors)
{
    public static ContractValidationResult Success { get; } =
        new(true, Array.Empty<ContractValidationError>());

    public static ContractValidationResult Failure(IReadOnlyList<ContractValidationError> errors) =>
        new(false, errors);
}

// Path is a JSON Pointer (RFC 6901), null only if the validator did not populate it.
public sealed record ContractValidationError(string? Path, string Code, string Message);
