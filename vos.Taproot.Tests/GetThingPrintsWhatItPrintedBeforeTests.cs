using System.Net;
using System.Text;
using FluentAssertions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

// The platform here answers a read of every Thing and a lookup by name alike, so each test holds whichever one the command asks.
[Collection(nameof(CliEnvVarCollection))]
public class GetThingPrintsWhatItPrintedBeforeTests
{
    private const string MyceliumUrl = "https://localhost:7243";

    private static readonly Guid Spring = Guid.Parse("0d6c1a0e-9f55-4a0e-8a59-0d3e7b0c2f44");
    private static readonly Guid AnotherSpring = Guid.Parse("5b8e2d1c-7a44-4f0b-9c3d-1e2f3a4b5c66");

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task<string> PrintedByAsync(string arguments, Func<HttpRequestMessage, HttpResponseMessage?> platform)
    {
        var mycelium = new MockHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == "/api/auth/token"
                ? Json("""{"token":"a-token"}""")
                : platform(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
        var writer = new StringWriter();
        await new GetCommandHandler(arguments, writer, new MyceliumClient(MyceliumUrl, "an-api-key", mycelium)).ExecuteAsync();
        return writer.ToString();
    }

    [Fact]
    public async Task A_name_more_than_one_Thing_holds_prints_each_identifier_with_the_name()
    {
        var printed = await PrintedByAsync("thing North", request => request.RequestUri!.PathAndQuery switch
        {
            "/api/things" => Json($$"""[{"Id":"{{Spring}}","Name":"North"},{"Id":"{{AnotherSpring}}","Name":"North"}]"""),
            "/api/things?name=North" => Json(
                $$"""{"error":"More than one Thing is named 'North'","matchingIds":["{{Spring}}","{{AnotherSpring}}"]}""",
                HttpStatusCode.Conflict),
            _ => null,
        });

        printed.Should().Be(
            "Error: Ambiguous name 'North': found 2 things with this name. Use the ID instead:\n"
            + $"  {Spring} - North\n"
            + $"  {AnotherSpring} - North"
            + Environment.NewLine);
    }

    [Fact]
    public async Task A_name_no_Thing_holds_prints_that_none_is_found()
    {
        var printed = await PrintedByAsync("thing North", request =>
            request.RequestUri!.PathAndQuery == "/api/things" ? Json("[]") : null);

        printed.Should().Be("Error: No thing found with name 'North'." + Environment.NewLine);
    }

    [Fact]
    public async Task A_name_one_Thing_holds_prints_the_Thing_as_the_platform_answers_it()
    {
        var printed = await PrintedByAsync("thing North", request => request.RequestUri!.PathAndQuery switch
        {
            "/api/things" => Json($$"""[{"Id":"{{Spring}}","Name":"North"}]"""),
            "/api/things?name=North" => Json($$"""{"Id":"{{Spring}}","Name":"North"}"""),
            var path when path == $"/api/things/{Spring}" => Json($$"""{"Id":"{{Spring}}","Name":"North","IsArchetype":false}"""),
            _ => null,
        });

        printed.Should().Be(
            "{" + Environment.NewLine
            + $"  \"Id\": \"{Spring}\"," + Environment.NewLine
            + "  \"Name\": \"North\"," + Environment.NewLine
            + "  \"IsArchetype\": false" + Environment.NewLine
            + "}" + Environment.NewLine);
    }

    [Fact]
    public async Task A_name_holding_a_comma_prints_the_Thing_that_holds_it()
    {
        var printed = await PrintedByAsync("thing North,upper", request => request.RequestUri!.PathAndQuery switch
        {
            "/api/things" => Json($$"""[{"Id":"{{Spring}}","Name":"North,upper"},{"Id":"{{AnotherSpring}}","Name":"North"}]"""),
            var path when path == $"/api/things/{Spring}" => Json($$"""{"Id":"{{Spring}}","Name":"North,upper"}"""),
            _ => null,
        });

        printed.Should().Contain("\"Name\": \"North,upper\"");
    }

    [Fact]
    public async Task An_identifier_the_platform_does_not_answer_prints_that_the_Thing_is_not_found()
    {
        var printed = await PrintedByAsync($"thing {Spring}", request =>
            request.RequestUri!.PathAndQuery == "/api/things" ? Json("[]") : null);

        printed.Should().Be($"Thing {Spring} not found" + Environment.NewLine);
    }
}
