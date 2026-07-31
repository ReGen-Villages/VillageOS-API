using System.Text.Json;
using FluentAssertions;
using vos.Service.Delta.Helpers;
using Xunit;

namespace vos.Service.Delta.Tests.Helpers;

public class JsonValueCoercionTests
{
    // ---------- CoerceToString ----------

    [Fact]
    public void CoerceToString_Null_ReturnsNull()
    {
        JsonValueCoercion.CoerceToString(null).Should().BeNull();
    }

    [Fact]
    public void CoerceToString_StringPrimitive_ReturnsString()
    {
        JsonValueCoercion.CoerceToString("hello").Should().Be("hello");
    }

    [Fact]
    public void CoerceToString_IntegerPrimitive_UsesToString()
    {
        JsonValueCoercion.CoerceToString(42).Should().Be("42");
    }

    [Fact]
    public void CoerceToString_JsonElementString_UnwrapsString()
    {
        var element = JsonDocument.Parse("\"world\"").RootElement;
        JsonValueCoercion.CoerceToString(element).Should().Be("world");
    }

    [Fact]
    public void CoerceToString_JsonElementNumber_ReturnsRawText()
    {
        var element = JsonDocument.Parse("123.45").RootElement;
        JsonValueCoercion.CoerceToString(element).Should().Be("123.45");
    }

    [Fact]
    public void CoerceToString_JsonElementTrue_ReturnsTrueLiteral()
    {
        var element = JsonDocument.Parse("true").RootElement;
        JsonValueCoercion.CoerceToString(element).Should().Be("true");
    }

    [Fact]
    public void CoerceToString_JsonElementFalse_ReturnsFalseLiteral()
    {
        var element = JsonDocument.Parse("false").RootElement;
        JsonValueCoercion.CoerceToString(element).Should().Be("false");
    }

    [Fact]
    public void CoerceToString_JsonElementNull_ReturnsNull()
    {
        var element = JsonDocument.Parse("null").RootElement;
        JsonValueCoercion.CoerceToString(element).Should().BeNull();
    }

    [Fact]
    public void CoerceToString_JsonElementArray_FallsBackToElementToString()
    {
        var element = JsonDocument.Parse("[1,2,3]").RootElement;
        JsonValueCoercion.CoerceToString(element).Should().Be("[1,2,3]");
    }

    [Fact]
    public void CoerceToString_JsonElementObject_FallsBackToElementToString()
    {
        var element = JsonDocument.Parse("{\"a\":1}").RootElement;
        JsonValueCoercion.CoerceToString(element).Should().Be("{\"a\":1}");
    }

    // ---------- TryGetPropertyValue ----------

    [Fact]
    public void TryGetPropertyValue_ExactKeyMatch_ReturnsTrue()
    {
        var props = new Dictionary<string, object> { ["url"] = "https://x" };

        var found = JsonValueCoercion.TryGetPropertyValue(props, "url", out var value);

        found.Should().BeTrue();
        value.Should().Be("https://x");
    }

    [Fact]
    public void TryGetPropertyValue_CaseInsensitiveFallback_ReturnsTrue()
    {
        var props = new Dictionary<string, object> { ["URL"] = "https://x" };

        var found = JsonValueCoercion.TryGetPropertyValue(props, "url", out var value);

        found.Should().BeTrue();
        value.Should().Be("https://x");
    }

    [Fact]
    public void TryGetPropertyValue_MissingKey_ReturnsFalseWithNullValue()
    {
        var props = new Dictionary<string, object> { ["other"] = "x" };

        var found = JsonValueCoercion.TryGetPropertyValue(props, "url", out var value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetPropertyValue_EmptyDictionary_ReturnsFalse()
    {
        var props = new Dictionary<string, object>();

        var found = JsonValueCoercion.TryGetPropertyValue(props, "anything", out var value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetPropertyValue_ExactMatchTakesPrecedenceOverCaseInsensitive()
    {
        var props = new Dictionary<string, object> { ["url"] = "lower", ["URL"] = "upper" };

        JsonValueCoercion.TryGetPropertyValue(props, "url", out var value).Should().BeTrue();

        value.Should().Be("lower");
    }

    // ---------- TryGetStringProperty ----------

    [Fact]
    public void TryGetStringProperty_StringValue_ReturnsTrueWithValue()
    {
        var props = new Dictionary<string, object> { ["name"] = "alice" };

        JsonValueCoercion.TryGetStringProperty(props, "name", out var value).Should().BeTrue();

        value.Should().Be("alice");
    }

    [Fact]
    public void TryGetStringProperty_JsonElementNumberValue_CoercesToRawText()
    {
        var props = new Dictionary<string, object>
        {
            ["count"] = JsonDocument.Parse("42").RootElement
        };

        JsonValueCoercion.TryGetStringProperty(props, "count", out var value).Should().BeTrue();

        value.Should().Be("42");
    }

    [Fact]
    public void TryGetStringProperty_MissingKey_ReturnsFalseWithNullValue()
    {
        var props = new Dictionary<string, object>();

        JsonValueCoercion.TryGetStringProperty(props, "missing", out var value).Should().BeFalse();

        value.Should().BeNull();
    }

    [Fact]
    public void TryGetStringProperty_CaseInsensitiveKey_ReturnsTrueWithCoercedValue()
    {
        var props = new Dictionary<string, object> { ["URL"] = "x" };

        JsonValueCoercion.TryGetStringProperty(props, "url", out var value).Should().BeTrue();

        value.Should().Be("x");
    }
}
