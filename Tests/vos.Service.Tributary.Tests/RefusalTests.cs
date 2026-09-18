using System.Text.Json;
using FluentAssertions;
using vos.Service.Tributary.Services;
using Xunit;

namespace vos.Service.Tributary.Tests;

// A refusal is read by two audiences — the caller, as the body's `error`, and the log, as the
// message. Both come from one string held once, so a change to one cannot miss the other.
public class RefusalTests
{
    [Fact]
    public void ToResult_TheCallerAndTheLogReadTheSameWords()
    {
        var result = new Refusal(400, "The endpoint is missing something.").ToResult();

        var error = result.Error.Should().BeOfType<JsonError>().Subject;
        error.StatusCode.Should().Be(400);
        error.Message.Should().Be("The endpoint is missing something.");
        BodyOf(error).GetProperty("error").GetString().Should().Be(error.Message);
    }

    [Fact]
    public void ToResult_DetailRidesAlongsideTheWordsWithoutReplacingThem()
    {
        var refusal = new Refusal(400, "Ambiguous.", new Dictionary<string, object?>
        {
            ["conflicts"] = new Dictionary<string, List<string>> { ["url"] = ["a.url", "b.url"] },
        });

        var body = BodyOf((JsonError)refusal.ToResult().Error!);

        body.GetProperty("error").GetString().Should().Be("Ambiguous.");
        body.GetProperty("conflicts").GetProperty("url").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("a.url", "b.url");
    }

    [Fact]
    public void ToResult_StatusOtherThan400_IsCarried()
    {
        var error = (JsonError)new Refusal(404, "Not here.").ToResult().Error!;

        error.StatusCode.Should().Be(404);
    }

    // Serialised the way the HTTP layer will serialise it, so the shape asserted is the shape sent.
    private static JsonElement BodyOf(JsonError error) =>
        JsonSerializer.SerializeToElement(error.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
