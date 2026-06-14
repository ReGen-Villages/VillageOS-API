using Microsoft.Extensions.Logging;
using NJsonSchema;
using NJsonSchema.Validation;

namespace vos.ManagedMicroservice.Shared.Contracts.Validation;

/// <summary>
/// Validates JSON payloads against a <see cref="JsonSchema"/> and produces a structured
/// <see cref="ContractValidationResult"/>. Stateless; safe to share.
/// </summary>
public sealed class SchemaValidator
{
    /// <summary>Validates <paramref name="json"/> against <paramref name="schema"/>.</summary>
    public ContractValidationResult Validate(string json, JsonSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(json);

        var errors = schema.Validate(json);
        if (errors.Count == 0)
            return ContractValidationResult.Success;

        var flat = new List<ContractValidationError>();
        foreach (var error in errors)
            FlattenInto(error, flat);
        return ContractValidationResult.Failure(flat);
    }

    /// <summary>Validates <paramref name="json"/> against <paramref name="schema"/>, throwing on failure.</summary>
    public void ValidateOrThrow(string json, JsonSchema schema, string schemaId)
    {
        var result = Validate(json, schema);
        if (!result.IsValid)
            throw new ContractValidationException(schemaId, result);
    }

    /// <summary>
    /// Validates <paramref name="json"/> against <paramref name="schema"/>; on failure emits a
    /// single <see cref="LogLevel.Warning"/> entry naming the schema, error count, and the
    /// first error's path/code/message. Never throws on a validation failure -- pairs with
    /// <see cref="ValidateOrThrow"/> so callers can pick a policy explicitly. Used by Release
    /// builds of <c>MyceliumClientBase</c> to keep production traffic flowing past stale schemas.
    /// </summary>
    public void ValidateForLog(string json, JsonSchema schema, string schemaId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var result = Validate(json, schema);
        if (result.IsValid) return;

        var first = result.Errors[0];
        logger.LogWarning(
            "Contract violation against {SchemaId}: {ErrorCount} error(s); first: {Path} [{Code}] {Message}",
            schemaId, result.Errors.Count, first.Path, first.Code, first.Message);
    }

    private static void FlattenInto(ValidationError error, List<ContractValidationError> sink)
    {
        if (error is ChildSchemaValidationError child && child.Errors.Count > 0)
        {
            foreach (var inner in child.Errors.Values.SelectMany(x => x))
                FlattenInto(inner, sink);
        }
        else
        {
            sink.Add(new ContractValidationError(error.Path, NormalizeCode(error.Kind), error.ToString()));
        }
    }

    // Format-keyword failures surface in NJsonSchema as e.g. UuidExpected / UriExpected — group them
    // under "Format" so the public code family matches the JSON Schema keyword, not the validator's
    // internal enum naming. Lookup keeps NormalizeCode branch count low.
    private static readonly HashSet<string> FormatExpectedKinds = new(StringComparer.Ordinal)
    {
        "UuidExpected", "UriExpected", "DateExpected", "DateTimeExpected",
        "TimeExpected", "TimeSpanExpected", "EmailExpected",
        "IpV4Expected", "IpV6Expected", "HostnameExpected", "GuidExpected"
    };

    private static readonly HashSet<string> TypeExpectedKinds = new(StringComparer.Ordinal)
    {
        "StringExpected", "IntegerExpected", "NumberExpected",
        "BooleanExpected", "ArrayExpected", "ObjectExpected", "NullExpected"
    };

    /// <summary>
    /// Maps NJsonSchema's <see cref="ValidationErrorKind"/> into a stable, public code family
    /// so the contract surface is decoupled from the underlying validator's enum naming.
    /// Unrecognised kinds fall through to the raw enum name.
    /// </summary>
    internal static string NormalizeCode(ValidationErrorKind kind)
    {
        var name = kind.ToString();
        if (name == nameof(ValidationErrorKind.PropertyRequired)) return "Required";
        if (name == nameof(ValidationErrorKind.NoAdditionalPropertiesAllowed)) return "AdditionalProperties";
        if (name == nameof(ValidationErrorKind.TooFewItems) || name == nameof(ValidationErrorKind.TooManyItems)) return "ArrayLength";
        if (FormatExpectedKinds.Contains(name)) return "Format";
        if (TypeExpectedKinds.Contains(name)) return "Type";
        return name;
    }
}
