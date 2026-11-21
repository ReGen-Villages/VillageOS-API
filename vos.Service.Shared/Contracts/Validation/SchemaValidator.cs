using Microsoft.Extensions.Logging;
using NJsonSchema;
using NJsonSchema.Validation;

namespace vos.Service.Shared.Contracts.Validation;

// Stateless; safe to share.
public sealed class SchemaValidator
{
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

    public void ValidateOrThrow(string json, JsonSchema schema, string schemaId)
    {
        var result = Validate(json, schema);
        if (!result.IsValid)
            throw new ContractValidationException(schemaId, result);
    }

    // Never throws on a validation failure, so a schema that has fallen behind never stops a live request.
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

    // NJsonSchema reports each format failure under its own kind, such as UuidExpected; they are grouped
    // under "Format" so the code a caller reads is the JSON Schema keyword, not the validator's name.
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

    // Maps NJsonSchema's kinds to a stable public code family; unknown kinds fall through to the raw name.
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
