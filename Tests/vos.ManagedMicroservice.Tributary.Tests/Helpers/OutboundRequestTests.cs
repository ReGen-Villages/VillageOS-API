using System.Text.Json;
using FluentAssertions;
using vos.ManagedMicroservice.Tributary.Helpers;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests.Helpers;

// Unit tests for OutboundRequest — pure construction of the outbound REST request from
// effective properties (Task #5469). Each base capability (headers, query params, content-type,
// timeout) is pinned in isolation here; end-to-end wiring is covered in HandleEndpointTests.
public class OutboundRequestTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    // ---------- MethodSupportsBody ----------

    [Theory]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    [InlineData("GET", false)]
    [InlineData("DELETE", false)]
    [InlineData("HEAD", false)]
    public void MethodSupportsBody_MatchesBodyBearingMethods(string method, bool expected)
    {
        OutboundRequest.MethodSupportsBody(method).Should().Be(expected);
    }

    // ---------- ApplyQueryParameters ----------

    [Fact]
    public void ApplyQueryParameters_Null_LeavesUriUnchanged()
    {
        var uri = new Uri("https://api.test/data");
        OutboundRequest.ApplyQueryParameters(uri, null).Should().Be(uri);
    }

    [Fact]
    public void ApplyQueryParameters_Empty_LeavesUriUnchanged()
    {
        var uri = new Uri("https://api.test/data");
        OutboundRequest.ApplyQueryParameters(uri, new Dictionary<string, string>()).Should().Be(uri);
    }

    [Fact]
    public void ApplyQueryParameters_AddsParamsToBareUri()
    {
        var uri = new Uri("https://api.test/q");
        var result = OutboundRequest.ApplyQueryParameters(uri, new Dictionary<string, string>
        {
            ["f"] = "json",
            ["outFields"] = "*"
        });

        // '*' is percent-encoded (%2A) — valid and accepted by ArcGIS; assert on the decoded form.
        var decoded = Uri.UnescapeDataString(result.Query);
        decoded.Should().Contain("f=json").And.Contain("outFields=*");
    }

    [Fact]
    public void ApplyQueryParameters_PreservesExistingQuery()
    {
        var uri = new Uri("https://api.test/q?where=1=1");
        var result = OutboundRequest.ApplyQueryParameters(uri, new Dictionary<string, string> { ["f"] = "json" });

        // The caller's existing query is preserved verbatim (not re-encoded); new params are appended.
        result.Query.Should().Contain("where=1=1").And.Contain("f=json");
        // Explicitly guard against double-encoding the existing query's '=' (1=1 -> 1%3D1).
        result.Query.Should().NotContain("%3D");
    }

    [Fact]
    public void ApplyQueryParameters_EncodesSpecialCharacters()
    {
        var uri = new Uri("https://api.test/q");
        var result = OutboundRequest.ApplyQueryParameters(uri, new Dictionary<string, string>
        {
            ["where"] = "name = 'a b'"
        });

        // Space and quote must be percent-encoded; a raw space would make an invalid request line.
        result.Query.Should().NotContain(" ");
        Uri.UnescapeDataString(result.Query).Should().Contain("name = 'a b'");
    }

    // ---------- ResolveTimeout ----------

    [Fact]
    public void ResolveTimeout_NumericSeconds_ReturnsThatDuration()
    {
        OutboundRequest.ResolveTimeout(Json("60")).Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ResolveTimeout_NumericStringSeconds_ReturnsThatDuration()
    {
        OutboundRequest.ResolveTimeout(Json("\"45\"")).Should().Be(TimeSpan.FromSeconds(45));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("\"nope\"")]
    [InlineData("true")]
    [InlineData("\"\"")]
    public void ResolveTimeout_InvalidOrNonPositive_FallsBackToDefault(string raw)
    {
        OutboundRequest.ResolveTimeout(Json(raw)).Should().Be(OutboundRequest.DefaultTimeout);
    }

    [Fact]
    public void ResolveTimeout_AtOrAboveTimeSpanMax_FallsBackToDefault()
    {
        // TimeSpan.FromSeconds(TimeSpan.MaxValue.TotalSeconds) itself overflows, so the boundary value
        // must be rejected rather than passed through.
        var atMax = TimeSpan.MaxValue.TotalSeconds.ToString("R");
        OutboundRequest.ResolveTimeout(Json(atMax)).Should().Be(OutboundRequest.DefaultTimeout);
    }

    // ---------- TryParseStringMap ----------

    [Fact]
    public void TryParseStringMap_Object_ParsesEntries()
    {
        var map = OutboundRequest.TryParseStringMap(Json("""{"Accept":"application/json","X-Api-Version":"2"}"""));

        map.Should().NotBeNull();
        map!["Accept"].Should().Be("application/json");
        map["X-Api-Version"].Should().Be("2");
    }

    [Fact]
    public void TryParseStringMap_IsCaseInsensitive()
    {
        var map = OutboundRequest.TryParseStringMap(Json("""{"Accept":"application/json"}"""));
        map!.ContainsKey("accept").Should().BeTrue();
    }

    [Fact]
    public void TryParseStringMap_NonStringValues_CoercedToRawText()
    {
        var map = OutboundRequest.TryParseStringMap(Json("""{"resultRecordCount":1000,"flag":true}"""));
        map!["resultRecordCount"].Should().Be("1000");
        map["flag"].Should().Be("true");
    }

    [Theory]
    [InlineData("\"a string\"")]
    [InlineData("42")]
    [InlineData("[1,2,3]")]
    public void TryParseStringMap_NotAnObject_ReturnsNull(string raw)
    {
        OutboundRequest.TryParseStringMap(Json(raw)).Should().BeNull();
    }

    // ---------- Build ----------

    [Fact]
    public void Build_GetWithoutBody_HasNoContent()
    {
        var req = OutboundRequest.Build("GET", new Uri("https://api.test/x"), default, null, null,
            OutboundRequest.DefaultContentType);

        req.Method.Should().Be(HttpMethod.Get);
        req.Content.Should().BeNull();
    }

    [Fact]
    public void Build_PostWithBody_SerializesJsonWithContentType()
    {
        var body = Json("""{"hello":"world"}""");
        var req = OutboundRequest.Build("POST", new Uri("https://api.test/x"), body, null, null, "application/xml");

        req.Method.Should().Be(HttpMethod.Post);
        req.Content.Should().NotBeNull();
        req.Content!.Headers.ContentType!.MediaType.Should().Be("application/xml");
        req.Content.ReadAsStringAsync().GetAwaiter().GetResult().Should().Contain("\"hello\":\"world\"");
    }

    [Fact]
    public void Build_PostWithUndefinedBody_HasNoContent()
    {
        var req = OutboundRequest.Build("POST", new Uri("https://api.test/x"), default, null, null,
            OutboundRequest.DefaultContentType);

        req.Content.Should().BeNull("an undefined body must not produce an empty content payload");
    }

    [Fact]
    public void Build_AppliesCustomHeaders()
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer abc", ["X-Api-Version"] = "2" };
        var req = OutboundRequest.Build("GET", new Uri("https://api.test/x"), default, headers, null,
            OutboundRequest.DefaultContentType);

        req.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer abc");
        req.Headers.GetValues("X-Api-Version").Should().ContainSingle().Which.Should().Be("2");
    }

    [Fact]
    public void Build_MergesQueryParametersIntoUri()
    {
        var query = new Dictionary<string, string> { ["f"] = "json" };
        var req = OutboundRequest.Build("GET", new Uri("https://api.test/x?a=1"), default, null, query,
            OutboundRequest.DefaultContentType);

        req.RequestUri!.Query.Should().Contain("a=1").And.Contain("f=json");
    }

    [Fact]
    public void Build_ContentHeaderInMap_DroppedNotThrown()
    {
        // Content-Type is a content header; adding it to request.Headers via the strict API would throw.
        // Build uses TryAddWithoutValidation, so a content header in the map on a bodyless request is
        // silently dropped (not applied, not thrown). A request header (Accept) IS applied.
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/plain",
            ["Accept"] = "application/json"
        };
        var req = OutboundRequest.Build("GET", new Uri("https://api.test/x"), default, headers, null,
            OutboundRequest.DefaultContentType);

        req.Content.Should().BeNull("a GET has no body, so the content header has nowhere to land");
        // Enumerate rather than Headers.Contains("Content-Type") — that overload itself throws
        // "Misused header name" for a content header. The point: it was dropped, not applied.
        req.Headers.Any(h => h.Key == "Content-Type").Should().BeFalse("content headers are not request headers");
        req.Headers.GetValues("Accept").Should().ContainSingle().Which.Should().Be("application/json");
    }

    [Fact]
    public void TryParseStringMap_NestedObjectOrArray_CoercedToRawJsonText()
    {
        var map = OutboundRequest.TryParseStringMap(Json("""{"obj":{"k":"v"},"arr":[1,2]}"""));

        map!["obj"].Should().Be("{\"k\":\"v\"}");
        map["arr"].Should().Be("[1,2]");
    }
}
