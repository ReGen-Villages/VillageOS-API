using FluentAssertions;
using vos.Service.Shared.Contracts.Validation;
using Xunit;

namespace vos.Service.Shared.Contracts.Tests;

public class SchemaValidatorTests
{
    private static readonly SchemaRegistry Registry = new();
    private static readonly SchemaValidator Validator = new();

    public static IEnumerable<object[]> PositiveFixtures() => new[]
    {
        new object[] { "mycelium-register-request",                "https://villageos/contracts/mycelium-register-request.schema.json",                "valid" },
        new object[] { "token-response",                          "https://villageos/contracts/token-response.schema.json",                         "valid" },
        new object[] { "handle-request-metabolism",              "https://villageos/contracts/handle-request-metabolism.schema.json",              "valid" },
        new object[] { "handle-request-metabolism",              "https://villageos/contracts/handle-request-metabolism.schema.json",              "valid-minimal" },
        new object[] { "apply-quantity-request",                 "https://villageos/contracts/apply-quantity-request.schema.json",                 "valid" },
        new object[] { "relationship-property-increment-request","https://villageos/contracts/relationship-property-increment-request.schema.json","valid" }
    };

    public static IEnumerable<object[]> NegativeFixtures() => new[]
    {
        new object[] { "mycelium-register-request",                "https://villageos/contracts/mycelium-register-request.schema.json",                "invalid-missing-required",   "Required" },
        new object[] { "mycelium-register-request",                "https://villageos/contracts/mycelium-register-request.schema.json",                "invalid-unknown-property",   "AdditionalProperties" },
        new object[] { "mycelium-register-request",                "https://villageos/contracts/mycelium-register-request.schema.json",                "invalid-wrong-type",         "Format" },
        new object[] { "token-response",                          "https://villageos/contracts/token-response.schema.json",                         "invalid-missing-required",   "Required" },
        new object[] { "token-response",                          "https://villageos/contracts/token-response.schema.json",                         "invalid-unknown-property",   "AdditionalProperties" },
        new object[] { "handle-request-metabolism",              "https://villageos/contracts/handle-request-metabolism.schema.json",              "invalid-missing-required",   "Required" },
        new object[] { "handle-request-metabolism",              "https://villageos/contracts/handle-request-metabolism.schema.json",              "invalid-unknown-property",   "AdditionalProperties" },
        new object[] { "handle-request-metabolism",              "https://villageos/contracts/handle-request-metabolism.schema.json",              "invalid-wrong-type",         "Type" },
        new object[] { "apply-quantity-request",                 "https://villageos/contracts/apply-quantity-request.schema.json",                 "invalid-missing-required",   "Required" },
        new object[] { "apply-quantity-request",                 "https://villageos/contracts/apply-quantity-request.schema.json",                 "invalid-unknown-property",   "AdditionalProperties" },
        new object[] { "apply-quantity-request",                 "https://villageos/contracts/apply-quantity-request.schema.json",                 "invalid-wrong-type",         "Type" },
        new object[] { "relationship-property-increment-request","https://villageos/contracts/relationship-property-increment-request.schema.json","invalid-missing-required",   "Required" },
        new object[] { "relationship-property-increment-request","https://villageos/contracts/relationship-property-increment-request.schema.json","invalid-wrong-type",         "Type" }
    };

    [Theory]
    [MemberData(nameof(PositiveFixtures))]
    public void Validate_PositiveFixture_ReturnsIsValid(string folder, string schemaId, string fixtureName)
    {
        var json = FixtureLoader.Read(folder, fixtureName);
        var schema = Registry.Get(schemaId);

        var result = Validator.Validate(json, schema);

        result.IsValid.Should().BeTrue($"fixture '{folder}/{fixtureName}' must satisfy schema {schemaId}; errors: {string.Join(", ", result.Errors.Select(e => $"{e.Path} {e.Code} {e.Message}"))}");
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(NegativeFixtures))]
    public void Validate_NegativeFixture_ReturnsFailureWithExpectedCodeFamily(string folder, string schemaId, string fixtureName, string expectedCodeFamily)
    {
        var json = FixtureLoader.Read(folder, fixtureName);
        var schema = Registry.Get(schemaId);

        var result = Validator.Validate(json, schema);

        result.IsValid.Should().BeFalse($"fixture '{folder}/{fixtureName}' must violate schema {schemaId}");
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(
            e => e.Code.Contains(expectedCodeFamily, StringComparison.OrdinalIgnoreCase),
            $"expected at least one error with code family '{expectedCodeFamily}', got: {string.Join(", ", result.Errors.Select(e => e.Code))}");
    }

    [Fact]
    public void ValidateOrThrow_OnFailure_ThrowsCarryingResult()
    {
        var schemaId = "https://villageos/contracts/token-response.schema.json";
        var json = FixtureLoader.Read("token-response", "invalid-missing-required");
        var schema = Registry.Get(schemaId);

        Action act = () => Validator.ValidateOrThrow(json, schema, schemaId);

        var ex = act.Should().Throw<ContractValidationException>().Which;
        ex.Result.IsValid.Should().BeFalse();
        ex.Result.Errors.Should().NotBeEmpty();
        ex.Message.Should().Contain(schemaId);
    }

    [Fact]
    public void ValidateOrThrow_OnSuccess_DoesNotThrow()
    {
        var schemaId = "https://villageos/contracts/token-response.schema.json";
        var json = FixtureLoader.Read("token-response", "valid");
        var schema = Registry.Get(schemaId);

        Action act = () => Validator.ValidateOrThrow(json, schema, schemaId);
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NullSchema_Throws()
    {
        Action act = () => Validator.Validate("{}", schema: null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("schema");
    }

    [Fact]
    public void Validate_NullJson_Throws()
    {
        var schema = Registry.Get("https://villageos/contracts/token-response.schema.json");
        Action act = () => Validator.Validate(json: null!, schema);
        act.Should().Throw<ArgumentNullException>().WithParameterName("json");
    }
}
