using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

public class TypedPropertiesTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private static JsonElement Written(object? value) =>
        JsonSerializer.SerializeToElement(TypedProperties.Typed(value));

    [Fact]
    public void A_value_is_written_with_its_type_beside_it()
    {
        var written = Written("running");

        written.GetProperty("typeInfo").GetString().Should().Be("vos.String");
        written.GetProperty("value").GetString().Should().Be("running");
    }

    [Fact]
    public void A_value_that_is_absent_is_still_written_with_a_type()
    {
        var written = Written(null);

        written.GetProperty("typeInfo").GetString().Should().Be("vos.String");
        written.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("\"filling\"", "vos.String")]
    [InlineData("true", "vos.Boolean")]
    [InlineData("false", "vos.Boolean")]
    [InlineData("21.5", "vos.Double")]
    [InlineData("null", "vos.String")]
    public void A_value_that_arrived_as_json_is_typed_by_what_it_is(string raw, string expected) =>
        TypedProperties.TypeNameFor(Json(raw)).Should().Be(expected);

    // A reading of 3 held as an integer is a property that cannot hold the 3.5 that follows it: the
    // first write decides the type and every later one is converted to it.
    [Fact]
    public void A_whole_number_is_a_double_like_any_other_number()
    {
        TypedProperties.TypeNameFor(Json("3")).Should().Be("vos.Double");
        TypedProperties.TypeNameFor(3).Should().Be("vos.Double");
        TypedProperties.TypeNameFor(3L).Should().Be("vos.Double");
    }

    // The broker reads a property of no other type as its raw JSON, so structure survives as text
    // rather than being refused.
    [Theory]
    [InlineData("""{"nested":1}""")]
    [InlineData("[1,2,3]")]
    public void A_value_with_structure_is_held_as_the_json_it_is(string raw)
    {
        var written = Written(Json(raw));

        written.GetProperty("typeInfo").GetString().Should().Be("vos.String");
        written.GetProperty("value").GetRawText().Should().Be(raw);
    }

    [Fact]
    public void Every_property_of_a_thing_is_written_the_same_way()
    {
        var written = JsonSerializer.SerializeToElement(TypedProperties.Typed(
            new Dictionary<string, object?> { ["url"] = "https://example", ["timeout"] = Json("30") }))!;

        written.GetProperty("url").GetProperty("typeInfo").GetString().Should().Be("vos.String");
        written.GetProperty("timeout").GetProperty("typeInfo").GetString().Should().Be("vos.Double");
    }

    // A create carrying no properties is one the broker takes as it stands, so nothing is invented
    // for it here.
    [Fact]
    public void A_thing_with_no_properties_is_written_with_none() =>
        TypedProperties.Typed((IReadOnlyDictionary<string, object?>?)null).Should().BeNull();
}
