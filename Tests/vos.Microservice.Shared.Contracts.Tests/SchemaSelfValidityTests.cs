using FluentAssertions;
using NJsonSchema;
using vos.Microservice.Shared.Contracts.Validation;
using Xunit;

namespace vos.Microservice.Shared.Contracts.Tests;

public class SchemaSelfValidityTests
{
    private static readonly SchemaRegistry Registry = new();

    public static IEnumerable<object[]> AllSchemaIds() =>
        Registry.Ids.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(AllSchemaIds))]
    public void Schema_IdMatchesProjectNamespace(string id)
    {
        // The registry rejects schemas with a missing $id at construction time; here we just pin the
        // naming convention so every schema lives under the same URI namespace.
        id.Should().StartWith("https://villageos/contracts/");
        id.Should().EndWith(".schema.json");
    }

    [Theory]
    [MemberData(nameof(AllSchemaIds))]
    public void Schema_ObjectSubschemas_DeclareAdditionalPropertiesFalse(string id)
    {
        var schema = Registry.Get(id);
        AssertObjectsForbidUnknownProperties(schema, path: "#");
    }

    [Fact]
    public void Registry_EagerlyLoadsAllEmbeddedSchemas()
    {
        Registry.Ids.Should().NotBeEmpty("schemas embedded under Contracts/Schemas/ must be discoverable at construction time");
        Registry.Ids.Should().Contain("https://villageos/contracts/broker-register-request.schema.json");
        Registry.Ids.Should().Contain("https://villageos/contracts/token-response.schema.json");
        Registry.Ids.Should().Contain("https://villageos/contracts/handle-request-metabolism.schema.json");
        Registry.Ids.Should().Contain("https://villageos/contracts/relationship-property-changed-event.schema.json");
    }

    [Fact]
    public void Registry_Get_UnknownId_Throws()
    {
        Action act = () => Registry.Get("https://villageos/contracts/does-not-exist.schema.json");
        act.Should().Throw<KeyNotFoundException>();
    }

    private static void AssertObjectsForbidUnknownProperties(JsonSchema schema, string path)
    {
        if (schema is null) return;

        var isObject = schema.Type.HasFlag(JsonObjectType.Object) || schema.Properties.Count > 0;
        if (isObject)
        {
            schema.AllowAdditionalProperties.Should().BeFalse(
                $"object subschema at '{path}' must set additionalProperties:false (strict-by-default project convention)");
        }

        foreach (var (name, prop) in schema.Properties)
            AssertObjectsForbidUnknownProperties(prop.ActualSchema, $"{path}/properties/{name}");

        if (schema.Item is not null)
            AssertObjectsForbidUnknownProperties(schema.Item.ActualSchema, $"{path}/items");

        for (var i = 0; i < schema.Items.Count; i++)
            AssertObjectsForbidUnknownProperties(schema.Items.ElementAt(i).ActualSchema, $"{path}/items/{i}");

        foreach (var (name, def) in schema.Definitions)
            AssertObjectsForbidUnknownProperties(def.ActualSchema, $"{path}/$defs/{name}");
    }
}
