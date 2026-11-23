namespace vos.Service.Shared.Contracts.Validation;

public sealed record ContractValidationResult(bool IsValid, IReadOnlyList<ContractValidationError> Errors)
{
    public static ContractValidationResult Success { get; } =
        new(true, Array.Empty<ContractValidationError>());

    public static ContractValidationResult Failure(IReadOnlyList<ContractValidationError> errors) =>
        new(false, errors);
}

// Path is a JSON Pointer such as /items/0 (RFC 6901), and null only where the validator gave none.
public sealed record ContractValidationError(string? Path, string Code, string Message);
