namespace vos.ManagedMicroservice.Shared.Contracts.Validation;

/// <summary>
/// Thrown by <see cref="SchemaValidator.ValidateOrThrow"/> when strict-mode validation fails.
/// Carries the structured <see cref="ContractValidationResult"/> so callers can map errors back to the wire.
/// </summary>
public sealed class ContractValidationException : Exception
{
    public ContractValidationResult Result { get; }

    public ContractValidationException(string schemaId, ContractValidationResult result)
        : base(BuildMessage(schemaId, result))
    {
        Result = result;
    }

    private static string BuildMessage(string schemaId, ContractValidationResult result)
    {
        if (result.IsValid)
            return $"ContractValidationException constructed with a passing result for schema '{schemaId}'.";

        var first = result.Errors.Count > 0 ? result.Errors[0] : null;
        var head = first is null
            ? $"Schema '{schemaId}' validation failed."
            : $"Schema '{schemaId}' validation failed at '{first.Path}' ({first.Code}): {first.Message}";

        return result.Errors.Count > 1
            ? $"{head} (+{result.Errors.Count - 1} more)"
            : head;
    }
}
