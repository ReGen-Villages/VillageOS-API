using FluentAssertions;
using vos.ManagedMicroservice.Shared.Contracts.Validation;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Contracts.Tests;

// Exercises the internal SchemaRegistry constructor that takes raw (resourceName, json)
// pairs. Routes coverage through the guard branches (missing $id, duplicate $id) without depending on
// the embedded-resource enumeration path.
public class SchemaRegistryInternalTests
{
    private const string ValidSchemaA = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://villageos/contracts/internal-test-a.schema.json",
          "type": "object",
          "additionalProperties": false,
          "required": ["x"],
          "properties": { "x": { "type": "string" } }
        }
        """;

    private const string ValidSchemaB = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://villageos/contracts/internal-test-b.schema.json",
          "type": "object",
          "additionalProperties": false
        }
        """;

    private const string MissingIdSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false
        }
        """;

    [Fact]
    public void Ctor_EmptyInput_LoadsZeroSchemas()
    {
        var reg = new SchemaRegistry(Array.Empty<(string, string)>());
        reg.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_TwoDistinctSchemas_LoadsBoth()
    {
        var reg = new SchemaRegistry(new[]
        {
            ("a.schema.json", ValidSchemaA),
            ("b.schema.json", ValidSchemaB)
        });

        reg.Ids.Should().BeEquivalentTo(new[]
        {
            "https://villageos/contracts/internal-test-a.schema.json",
            "https://villageos/contracts/internal-test-b.schema.json"
        });
    }

    [Fact]
    public void Ctor_SchemaMissingDollarId_Throws()
    {
        Action act = () => _ = new SchemaRegistry(new[] { ("bad.schema.json", MissingIdSchema) });
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*bad.schema.json*$id*");
    }

    [Fact]
    public void Ctor_DuplicateDollarId_Throws()
    {
        Action act = () => _ = new SchemaRegistry(new[]
        {
            ("first.schema.json",  ValidSchemaA),
            ("second.schema.json", ValidSchemaA)
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate*second.schema.json*");
    }

    [ContractSchema("https://villageos/contracts/mycelium-register-request.schema.json")]
    private sealed record TaggedDto;

    private sealed record UntaggedDto;

    [Fact]
    public void Get_OfT_TaggedType_ReturnsSchemaFromAttributeId()
    {
        var reg = new SchemaRegistry();
        var schema = reg.Get<TaggedDto>();
        schema.Should().NotBeNull();
    }

    [Fact]
    public void Get_OfT_UntaggedType_Throws()
    {
        var reg = new SchemaRegistry();
        Action act = () => reg.Get<UntaggedDto>();
        act.Should().Throw<InvalidOperationException>().WithMessage("*ContractSchema*");
    }
}
