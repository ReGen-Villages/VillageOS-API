using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared;
using Xunit;

namespace vos.Service.Shared.Tests;

// The one shared JSONata engine wiring, used by both Tributary (endpoint transforms) and Phloem
// (on-wire transforms). Both call this type, so these tests cover the one evaluator.
public class JsonataTransformTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact]
    public void Validate_returns_null_for_a_valid_expression()
    {
        JsonataTransform.Validate("{\"name\": firstName & \" \" & lastName}").Should().BeNull();
    }

    [Fact] // A syntax error is reported, not thrown to the caller
    public void Validate_returns_the_error_for_an_invalid_expression()
    {
        JsonataTransform.Validate("this is ( not valid").Should().NotBeNullOrEmpty();
    }

    [Fact] // The expression reshapes the input value
    public void Eval_reshapes_a_json_value()
    {
        var transform = new JsonataTransform("{\"name\": firstName & \" \" & lastName}");
        var result = transform.Eval(Json("{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\"}"));
        result.GetProperty("name").GetString().Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Eval_can_select_and_compute_fields()
    {
        var transform = new JsonataTransform("{\"total\": a + b}");
        var result = transform.Eval(Json("{\"a\":2,\"b\":40}"));
        result.GetProperty("total").GetInt32().Should().Be(42);
    }

    [Fact]
    public void Ctor_throws_on_an_invalid_expression()
    {
        var act = () => new JsonataTransform("{{{");
        act.Should().Throw<Exception>();
    }
}
