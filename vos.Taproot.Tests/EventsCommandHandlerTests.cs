using System.Runtime.CompilerServices;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class EventsCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();
    private static readonly DateTime Noon = new(2026, 9, 20, 12, 0, 0);

    private Task Execute(string arg) =>
        new EventsCommandHandler(arg, _writer, _myceliumMock.Object, () => Noon).ExecuteAsync();

    [Fact]
    public async Task Execute_WithoutWatch_ShowsUsage()
    {
        await Execute("");
        await Execute("listen");

        _writer.ToString().Should().Contain("Usage:").And.Contain("events watch");
    }

    [Fact]
    public async Task Watch_PrintsEachEventWithItsTimeNameAndPayloadAndCountsThem()
    {
        _myceliumMock.Setup(c => c.WatchEventsAsync(It.IsAny<CancellationToken>()))
            .Returns(Events(new ServerSentEvent("ThingCreated", "{\"Id\":\"a\"}"), new ServerSentEvent("PropertyChanged", "{\"Id\":\"b\"}")));

        await Execute("watch --for=5");

        _writer.ToString().Should().Be(
            "12:00:00  ThingCreated  {\"Id\":\"a\"}\n"
            + "12:00:00  PropertyChanged  {\"Id\":\"b\"}\n"
            + "Watched 2 event(s) in 5 s.\n");
    }

    [Fact]
    public async Task Watch_EndsWhenTheWindowClosesAndSaysWhenNothingArrived()
    {
        _myceliumMock.Setup(c => c.WatchEventsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) => Endless(token));

        await Execute("watch --for=1");

        _writer.ToString().Should().Be("Nothing happened in 1 s.\n");
    }

    [Fact]
    public async Task Watch_WhenTheStreamCannotBeOpened_WritesTheOperatorMessage()
    {
        _myceliumMock.Setup(c => c.WatchEventsAsync(It.IsAny<CancellationToken>()))
            .Returns(Throwing(new HttpRequestException("offline")));

        await Execute("watch");

        _writer.ToString().Should().Contain("Error:").And.Contain("offline").And.NotContain("Nothing happened");
    }

    private static async IAsyncEnumerable<ServerSentEvent> Events(params ServerSentEvent[] events)
    {
        foreach (var one in events)
        {
            await Task.Yield();
            yield return one;
        }
    }

    private static async IAsyncEnumerable<ServerSentEvent> Endless([EnumeratorCancellation] CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        yield break;
    }

    private static async IAsyncEnumerable<ServerSentEvent> Throwing(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
