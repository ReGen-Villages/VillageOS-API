using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace vos.Service.Shared.Tests;

public class JsonScalarTests
{
    public static TheoryData<string, object?> Scalars => new()
    {
        { "\"spring\"", "spring" },
        { "12", 12L },
        { "-3", -3L },
        { "12.5", 12.5 },
        { "1e3", 1000.0 },
        { "true", true },
        { "false", false },
        { "null", null },
    };

    [Theory]
    [MemberData(nameof(Scalars))]
    public void A_string_number_true_false_or_null_reads_as_the_plain_value(string json, object? expected)
    {
        JsonScalar.TryRead(JsonDocument.Parse(json).RootElement, out var value).Should().BeTrue();
        value.Should().Be(expected);
    }

    [Fact]
    public void A_whole_number_beyond_a_long_reads_as_a_double()
    {
        JsonScalar.TryRead(JsonDocument.Parse("1e20").RootElement, out var value).Should().BeTrue();
        value.Should().Be(1e20);
    }

    [Theory]
    [InlineData("""{"a":1}""")]
    [InlineData("[1,2]")]
    public void An_object_or_a_list_is_no_scalar(string json)
    {
        JsonScalar.TryRead(JsonDocument.Parse(json).RootElement, out var value).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void An_element_holding_nothing_is_no_scalar()
    {
        JsonScalar.TryRead(default, out _).Should().BeFalse();
    }
}
