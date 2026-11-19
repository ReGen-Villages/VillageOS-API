using System.Net;
using System.Text;
using FluentAssertions;
using vos.PlatformClient;
using vos.Tests.Shared;
using Xunit;

namespace vos.Taproot.Tests;

[Collection(nameof(CliEnvVarCollection))]
public class ANameAnAddressWouldLoseIsRefusedTests
{
    private const string MyceliumUrl = "https://localhost:7243";

    private static readonly Guid Spring = Guid.Parse("0d6c1a0e-9f55-4a0e-8a59-0d3e7b0c2f44");

    private static readonly string[] EveryMethodThatPutsANameIntoAnAddress =
    [
        "DeleteProperty", "DeleteRelationshipProperty", "LoadLibrarySeed", "SaveLibrarySeed", "PostToEndpoint",
        "GetPropertyMode", "SetPropertyMode", "GetPropertyVersions", "GetRange", "DeleteRange", "GetStateOccurrences",
        "GetThingsInState",
    ];

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (MyceliumClient Client, MockHttpMessageHandler Mycelium) APlatform()
    {
        var mycelium = new MockHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == "/api/auth/token" ? Json("""{"token":"a-token"}""") : Json("{}"));
        return (new MyceliumClient(MyceliumUrl, "an-api-key", mycelium), mycelium);
    }

    private static IEnumerable<HttpRequestMessage> AskedOf(MockHttpMessageHandler mycelium) =>
        mycelium.Requests.Where(request => request.RequestUri!.AbsolutePath != "/api/auth/token");

    private static bool AnAddressLoses(string step) =>
        new Uri($"http://host/api/a/{step}/b").AbsolutePath != $"/api/a/{step}/b";

    private static bool AnAddressLeavesTheApi(string path) =>
        !new Uri($"http://host{path}").AbsolutePath.StartsWith("/api/", StringComparison.Ordinal);

    private static Task Asking(MyceliumClient client, string method, string name) => method switch
    {
        "DeleteProperty" => client.DeletePropertyAsync(Spring, name),
        "DeleteRelationshipProperty" => client.DeleteRelationshipPropertyAsync(Spring, name),
        "LoadLibrarySeed" => client.LoadLibrarySeedAsync(name),
        "SaveLibrarySeed" => client.SaveLibrarySeedAsync(name),
        "PostToEndpoint" => client.PostToEndpointAsync(name, "{}"),
        "GetPropertyMode" => client.GetPropertyModeAsync(Spring, name),
        "SetPropertyMode" => client.SetPropertyModeAsync(Spring, name, "Latest"),
        "GetPropertyVersions" => client.GetPropertyVersionsAsync(Spring, name),
        "GetRange" => client.GetRangeAsync(Spring, name),
        "DeleteRange" => client.DeleteRangeAsync(Spring, name),
        "GetStateOccurrences" => client.GetStateOccurrencesAsync(Spring, name, null, null),
        "GetThingsInState" => client.GetThingsInStateAsync(name),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "no such method in this test"),
    };

    public static TheoryData<string, string> EveryMethodWithEachNameAnAddressLoses()
    {
        var data = new TheoryData<string, string>();
        foreach (var method in EveryMethodThatPutsANameIntoAnAddress)
            foreach (var name in new[] { ".", ".." })
                data.Add(method, name);
        return data;
    }

    public static TheoryData<string> EveryMethod() => new(EveryMethodThatPutsANameIntoAnAddress);

    [Theory]
    [MemberData(nameof(EveryMethodWithEachNameAnAddressLoses))]
    public async Task A_name_an_address_would_lose_is_refused_and_nothing_is_sent(string method, string name)
    {
        AnAddressLoses(name).Should().BeTrue();
        var (client, mycelium) = APlatform();

        var asking = () => Asking(client, method, name);

        (await asking.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("'.' or '..'");
        AskedOf(mycelium).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(EveryMethod))]
    public async Task An_empty_name_is_refused_and_nothing_is_sent(string method)
    {
        var (client, mycelium) = APlatform();

        var asking = () => Asking(client, method, "");

        (await asking.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("empty");
        AskedOf(mycelium).Should().BeEmpty();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("....")]
    [InlineData("a.b")]
    public async Task The_client_and_the_route_check_refuse_a_step_exactly_when_an_address_would_lose_it(string step)
    {
        var (client, _) = APlatform();

        var asking = () => client.GetPropertyModeAsync(Spring, step);
        var checking = () => PlatformRoute.AddressOf(MyceliumUrl, $"/api/a/{step}/b");

        if (AnAddressLoses(step))
        {
            await asking.Should().ThrowAsync<ArgumentException>();
            checking.Should().Throw<ArgumentException>();
        }
        else
        {
            await asking.Should().NotThrowAsync();
            checking.Should().NotThrow();
        }
    }

    [Theory]
    [InlineData("..x", "..x")]
    [InlineData("...", "...")]
    [InlineData("....", "....")]
    [InlineData("flow m3/h", "flow%20m3%2Fh")]
    [InlineData("a.b", "a.b")]
    public async Task A_name_an_address_keeps_is_sent_escaped(string name, string sent)
    {
        var (client, mycelium) = APlatform();

        await client.GetPropertyModeAsync(Spring, name);

        AskedOf(mycelium).Should().ContainSingle()
            .Which.RequestUri!.AbsolutePath.Should().Be($"/api/things/{Spring}/properties/{sent}/mode");
    }

    [Theory]
    [InlineData("%2E%2E")]
    [InlineData("%2e.")]
    [InlineData(".%2e")]
    [InlineData("%252e%252e")]
    public void The_route_check_refuses_an_encoded_step_exactly_when_the_address_would_leave_the_api(string step)
    {
        var path = $"/api/{step}/things";

        var checking = () => PlatformRoute.AddressOf(MyceliumUrl, path);

        if (AnAddressLeavesTheApi(path))
            checking.Should().Throw<ArgumentException>();
        else
            checking.Should().NotThrow();
    }

    [Theory]
    [InlineData("..%2F")]
    [InlineData("%5C")]
    public void The_route_check_refuses_a_step_holding_an_encoded_slash_or_backslash_though_the_address_stays_in_the_api(string step)
    {
        var path = $"/api/{step}/things";
        AnAddressLeavesTheApi(path).Should().BeFalse();

        var checking = () => PlatformRoute.AddressOf(MyceliumUrl, path);

        checking.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Deleting_a_property_named_two_dots_from_the_command_line_prints_the_refusal_and_deletes_nothing()
    {
        var (client, mycelium) = APlatform();
        var writer = new StringWriter();

        await new DeleteCommandHandler($"property {Spring} ..", writer, client).ExecuteAsync();

        writer.ToString().Should().StartWith("Error:").And.Contain("'.' or '..'");
        AskedOf(mycelium).Should().NotContain(request => request.Method == HttpMethod.Delete);
    }
}
