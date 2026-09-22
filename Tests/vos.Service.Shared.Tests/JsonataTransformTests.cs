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

    private static readonly DateTimeOffset ModelInstant = new(2019, 4, 1, 9, 0, 0, TimeSpan.Zero);

    private static ModelClock AnchoredClock()
    {
        var clock = new ModelClock();
        clock.AnchorTo(ModelInstant, rate: 0);
        return clock;
    }

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
        var result = transform.Eval(Json("{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\"}"), TimeProvider.System);
        result.GetProperty("name").GetString().Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Eval_can_select_and_compute_fields()
    {
        var transform = new JsonataTransform("{\"total\": a + b}");
        var result = transform.Eval(Json("{\"a\":2,\"b\":40}"), TimeProvider.System);
        result.GetProperty("total").GetInt32().Should().Be(42);
    }

    [Fact]
    public void Ctor_throws_on_an_invalid_expression()
    {
        var act = () => new JsonataTransform("{{{");
        act.Should().Throw<Exception>();
    }

    // The fault this guards: a model's transform stamped a value with the instant on the machine running
    // the service, so a run anchored years back wrote this year onto values the model holds.
    [Fact]
    public void The_current_instant_is_read_from_the_clock_the_caller_stamps_with()
    {
        var transform = new JsonataTransform("{\"assessedOn\": $now()}");

        var result = transform.Eval(Json("{}"), AnchoredClock());

        result.GetProperty("assessedOn").GetString().Should().Be("2019-04-01T09:00:00.000Z");
    }

    [Fact] // $millis() is the same instant counted differently, so it reads the same clock
    public void The_instant_in_milliseconds_agrees_with_the_current_instant()
    {
        var transform = new JsonataTransform("{\"millis\": $millis(), \"fromMillis\": $fromMillis($millis())}");

        var result = transform.Eval(Json("{}"), AnchoredClock());

        result.GetProperty("millis").GetInt64().Should().Be(ModelInstant.ToUnixTimeMilliseconds());
        result.GetProperty("fromMillis").GetString().Should().Be("2019-04-01T09:00:00.000Z");
    }

    [Fact] // An un-anchored model clock is this machine's, which is what the model's own is until simulated
    public void An_unanchored_clock_reads_this_machine()
    {
        var transform = new JsonataTransform("{\"at\": $now()}");

        var result = transform.Eval(Json("{}"), new ModelClock());

        DateTimeOffset.Parse(result.GetProperty("at").GetString()!)
            .Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }
}
