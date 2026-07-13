using Microsoft.Extensions.Logging;
using NJsonSchema;
using NJsonSchema.Validation;

namespace vos.ManagedMicroservice.Shared.Contracts.Validation;

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

    // Never throws on validation failure; lets production traffic flow past stale schemas.
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

    // NJsonSchema reports format failures as UuidExpected/UriExpected/etc; group under "Format"
    // so the public code matches the JSON Schema keyword, not the validator's enum naming.
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
