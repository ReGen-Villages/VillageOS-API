using System.Text.Json;
using FluentAssertions;
using vos.Service.Metabolism.Helpers;
using Xunit;

namespace vos.Service.Metabolism.Tests.Helpers;

public class JsonValueUnwrapperTests
{
    [Fact]
    public void Unwrap_Null_ReturnsNull()
    {
        JsonValueUnwrapper.Unwrap(null).Should().BeNull();
    }

    [Fact]
    public void Unwrap_NonJsonElementValue_PassesThroughUnchanged()
    {
        JsonValueUnwrapper.Unwrap("plain string").Should().Be("plain string");
        JsonValueUnwrapper.Unwrap(42).Should().Be(42);
        JsonValueUnwrapper.Unwrap(3.14).Should().Be(3.14);
    }

    [Fact]
    public void Unwrap_JsonElementInt32Range_ReturnsInt()
    {
        var element = JsonDocument.Parse("123").RootElement;

        var result = JsonValueUnwrapper.Unwrap(element);

        result.Should().BeOfType<int>();
        result.Should().Be(123);
    }

    [Fact]
    public void Unwrap_JsonElementInt64Range_ReturnsLong()
    {
        var beyondInt32 = (long)int.MaxValue + 100;
        var element = JsonDocument.Parse(beyondInt32.ToString()).RootElement;

        var result = JsonValueUnwrapper.Unwrap(element);

        result.Should().BeOfType<long>();
        result.Should().Be(beyondInt32);
    }

    [Fact]
    public void Unwrap_JsonElementFractional_ReturnsDecimal()
    {
        var element = JsonDocument.Parse("3.14159").RootElement;

        var result = JsonValueUnwrapper.Unwrap(element);

        result.Should().BeOfType<decimal>();
        result.Should().Be(3.14159m);
    }

    [Fact]
    public void Unwrap_JsonElementString_ReturnsString()
    {
        var element = JsonDocument.Parse("\"hello\"").RootElement;

        JsonValueUnwrapper.Unwrap(element).Should().Be("hello");
    }

    [Fact]
    public void Unwrap_JsonElementTrue_ReturnsTrueBool()
    {
        var element = JsonDocument.Parse("true").RootElement;

        var result = JsonValueUnwrapper.Unwrap(element);

        result.Should().BeOfType<bool>();
        result.Should().Be(true);
    }

    [Fact]
    public void Unwrap_JsonElementFalse_ReturnsFalseBool()
    {
        var element = JsonDocument.Parse("false").RootElement;

        var result = JsonValueUnwrapper.Unwrap(element);

        result.Should().BeOfType<bool>();
        result.Should().Be(false);
    }

    [Fact]
    public void Unwrap_JsonElementNullKind_ReturnsNull()
    {
        var element = JsonDocument.Parse("null").RootElement;

        JsonValueUnwrapper.Unwrap(element).Should().BeNull();
    }

    [Fact]
    public void Unwrap_JsonElementArray_ReturnsRawText()
    {
        var element = JsonDocument.Parse("[1,2,3]").RootElement;

        JsonValueUnwrapper.Unwrap(element).Should().Be("[1,2,3]");
    }

    [Fact]
    public void Unwrap_JsonElementObject_ReturnsRawText()
    {
        var element = JsonDocument.Parse("{\"a\":1}").RootElement;

        JsonValueUnwrapper.Unwrap(element).Should().Be("{\"a\":1}");
    }
}
