using System.Text.Json;
using FluentAssertions;
using vos.ManagedMicroservice.Phloem.Execution;
using Xunit;

namespace vos.ManagedMicroservice.Phloem.Tests;

// Field-level wire mapping (#5874): extract a dotted from-path, place at a dotted to-path, deep-merge several
// contributions into one input. Pure functions, tested without the executor.
public class PayloadMappingTests
{
    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement;

    [Fact] // TC #5882: a from-path extracts a single field of the upstream output
    public void Extract_dotted_path_returns_the_nested_field()
    {
        var value = Json("{\"user\":{\"id\":7,\"name\":\"Ada\"}}");
        PayloadMapping.Extract(value, "user.id")!.Value.GetInt32().Should().Be(7);
        PayloadMapping.Extract(value, "user.name")!.Value.GetString().Should().Be("Ada");
    }

    [Fact] // TC #5884: an empty path carries the whole payload
    public void Extract_empty_path_returns_the_whole_value()
    {
        var value = Json("{\"a\":1}");
        PayloadMapping.Extract(value, "").ToString().Should().Be(value.ToString());
    }

    [Fact]
    public void Extract_missing_path_returns_null()
    {
        PayloadMapping.Extract(Json("{\"a\":1}"), "a.b.c").Should().BeNull();
        PayloadMapping.Extract(Json("{\"a\":1}"), "missing").Should().BeNull();
    }

    [Fact]
    public void Place_wraps_the_value_in_nested_objects_for_the_path()
    {
        var placed = PayloadMapping.Place("x.y", Json("42"));
        PayloadMapping.ToElement(placed).ToString().Should().Be(Json("{\"x\":{\"y\":42}}").ToString());
    }

    [Fact]
    public void Place_empty_path_returns_the_value_unwrapped()
    {
        PayloadMapping.ToElement(PayloadMapping.Place("", Json("\"hi\""))).GetString().Should().Be("hi");
    }

    [Fact] // TC #5883: two contributions deep-merge instead of overwriting
    public void Merge_combines_object_keys_recursively()
    {
        var a = PayloadMapping.Place("a", Json("1"));       // {"a":1}
        var b = PayloadMapping.Place("b", Json("2"));       // {"b":2}
        var merged = PayloadMapping.Merge(a, b);
        PayloadMapping.ToElement(merged).ToString().Should().Be(Json("{\"a\":1,\"b\":2}").ToString());
    }

    [Fact]
    public void Merge_nested_objects_combine_and_a_scalar_clash_takes_the_incoming_value()
    {
        var existing = PayloadMapping.Place("", Json("{\"outer\":{\"a\":1},\"keep\":9}"));
        var incoming = PayloadMapping.Place("", Json("{\"outer\":{\"b\":2},\"keep\":10}"));
        var merged = PayloadMapping.Merge(existing, incoming);
        // outer.a and outer.b both survive; the scalar clash on `keep` resolves last-wins.
        PayloadMapping.ToElement(merged).ToString().Should().Be(Json("{\"outer\":{\"a\":1,\"b\":2},\"keep\":10}").ToString());
    }
}
