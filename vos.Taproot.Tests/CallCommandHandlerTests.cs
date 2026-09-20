using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class CallCommandHandlerTests
{
    private static readonly Guid HandlerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Mock<MyceliumClient> _myceliumMock = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private Task Execute(string arg) => new CallCommandHandler(arg, _writer, _myceliumMock.Object).ExecuteAsync();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("")]
    [InlineData("endpoint echo")]
    [InlineData("elsewhere echo {}")]
    public async Task Execute_WithoutATargetNameAndBody_ShowsUsage(string arg)
    {
        await Execute(arg);

        _writer.ToString().Should().Contain("Usage:").And.Contain("call endpoint").And.Contain("call service");
    }

    [Fact]
    public async Task Endpoint_PostsTheBodyAsTypedAndPrintsAJsonAnswerFormatted()
    {
        _myceliumMock.Setup(c => c.PostToEndpointAsync("echo", "{\"say\": \"hello\"}"))
            .ReturnsAsync("{\"said\":\"hello\"}");

        await Execute("endpoint echo {\"say\": \"hello\"}");

        _writer.ToString().Should().Be("{\n  \"said\": \"hello\"\n}\n");
    }

    [Fact]
    public async Task Endpoint_PrintsAnAnswerThatIsNotJsonAsItCame()
    {
        _myceliumMock.Setup(c => c.PostToEndpointAsync("echo", "{}")).ReturnsAsync("accepted");

        await Execute("endpoint echo {}");

        _writer.ToString().Should().Be("accepted\n");
    }

    [Fact]
    public async Task Endpoint_WhenTheBodyIsNotJson_SendsNothingAndSaysSo()
    {
        await Execute("endpoint echo {not json");

        _writer.ToString().Should().Contain("Error: the body is not JSON");
        _myceliumMock.Verify(c => c.PostToEndpointAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Endpoint_WhenTheRouteRefuses_WritesItsWords()
    {
        _myceliumMock.Setup(c => c.PostToEndpointAsync("ghost", "{}"))
            .ThrowsAsync(new HttpRequestException("404 Not Found: {\"error\":\"No endpoint service registered for subdomain 'ghost'\"}"));

        await Execute("endpoint ghost {}");

        _writer.ToString().Should().Contain("Error:").And.Contain("No endpoint service registered for subdomain 'ghost'");
    }

    [Fact]
    public async Task Service_ResolvesTheHandlerByNameAndPostsTheBody()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync())
            .ReturnsAsync(Parse($"[{{\"Id\":\"{HandlerId}\",\"Name\":\"Metabolism\"}}]"));
        _myceliumMock.Setup(c => c.RequestServiceAsync(HandlerId, "{\"tick\":1}")).ReturnsAsync("{\"ok\":true}");

        await Execute("service Metabolism {\"tick\":1}");

        _writer.ToString().Should().Be("{\n  \"ok\": true\n}\n");
    }

    [Fact]
    public async Task Service_TakesAHandlerIdTheModelDoesNotName()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse("[]"));
        _myceliumMock.Setup(c => c.RequestServiceAsync(HandlerId, "{}")).ReturnsAsync("done");

        await Execute($"service {HandlerId} {{}}");

        _writer.ToString().Should().Be("done\n");
    }

    [Fact]
    public async Task Service_WhenTheNameResolvesToNothing_SaysSoAndSendsNothing()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync()).ReturnsAsync(Parse("[]"));

        await Execute("service Nobody {}");

        _writer.ToString().Should().Contain("Error:");
        _myceliumMock.Verify(c => c.RequestServiceAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }
}
