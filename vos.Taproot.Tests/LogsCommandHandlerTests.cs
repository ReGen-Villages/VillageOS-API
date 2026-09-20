using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

[Collection(nameof(WorkingDirectoryCollection))]
public class LogsCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private Task Execute(string arg) => new LogsCommandHandler(arg, _writer, _myceliumMock.Object).ExecuteAsync();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Execute_NoSubcommand_ShowsUsage()
    {
        await Execute("");

        _writer.ToString().Should().Contain("Usage:").And.Contain("logs tail").And.Contain("logs follow").And.Contain("logs download");
    }

    [Fact]
    public async Task Tail_AsksForTheLinesAndServiceGivenAndWritesEveryLine()
    {
        _myceliumMock.Setup(c => c.GetLogTailAsync(50, "irrigator"))
            .ReturnsAsync(Parse("{\"file\":\"watch-irrigator.log\",\"lines\":[\"one\",\"two\"]}"));

        await Execute("tail --lines=50 --service=irrigator");

        _writer.ToString().Should().Be("watch-irrigator.log (last 2 lines):\none\ntwo\n");
    }

    [Fact]
    public async Task Tail_WithNoOptions_AsksForTheMyceliumLogWithNoLineCount()
    {
        _myceliumMock.Setup(c => c.GetLogTailAsync(null, null))
            .ReturnsAsync(Parse("{\"file\":\"mycelium-20260920.log\",\"lines\":[]}"));

        await Execute("tail");

        _writer.ToString().Should().StartWith("mycelium-20260920.log (last 0 lines):");
    }

    [Fact]
    public async Task Tail_WhenThePlatformHasNoFile_SaysSo()
    {
        _myceliumMock.Setup(c => c.GetLogTailAsync(null, "ghost")).ReturnsAsync(Parse("{\"file\":null,\"lines\":[]}"));

        await Execute("tail --service=ghost");

        _writer.ToString().Should().Contain("No log file found.");
    }

    [Fact]
    public async Task Follow_WritesEachLogLineAsItArrivesAndReturnsWhenTheStreamEnds()
    {
        _myceliumMock.Setup(c => c.FollowLogAsync(null, "irrigator", It.IsAny<CancellationToken>()))
            .Returns(Events(new ServerSentEvent("log", "\"one\""), new ServerSentEvent("message", "ignored"), new ServerSentEvent("log", "\"two\"")));

        await Execute("follow --service=irrigator --for=5");

        _writer.ToString().Should().Be("one\ntwo\nFollowed for 5 s.\n");
    }

    [Fact]
    public async Task Follow_EndsWhenTheWindowClosesOnAStreamThatNeverEnds()
    {
        _myceliumMock.Setup(c => c.FollowLogAsync(null, null, It.IsAny<CancellationToken>()))
            .Returns((int? _, string? _, CancellationToken token) => EndlessAfter(token, new ServerSentEvent("log", "\"only\"")));

        await Execute("follow --for=1");

        _writer.ToString().Should().Be("only\nFollowed for 1 s.\n");
    }

    [Fact]
    public async Task Download_SavesTheFileUnderThePlatformsNameOrTheOneGiven()
    {
        var directory = Directory.CreateTempSubdirectory("taproot-logs-");
        try
        {
            _myceliumMock.Setup(c => c.DownloadLogAsync("irrigator"))
                .ReturnsAsync(() => new LogDownload("watch-irrigator.log", new MemoryStream(Encoding.UTF8.GetBytes("line\n"))));
            var named = Path.Combine(directory.FullName, "taken.log");

            await Execute($"download --service=irrigator {named}");

            File.ReadAllText(named).Should().Be("line\n");
            _writer.ToString().Should().Contain($"Log saved to {named}");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Download_WithNoFileGiven_UsesThePlatformsFileName()
    {
        var directory = Directory.CreateTempSubdirectory("taproot-logs-");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(directory.FullName);
            _myceliumMock.Setup(c => c.DownloadLogAsync(null))
                .ReturnsAsync(() => new LogDownload("mycelium-20260920.log", new MemoryStream(Encoding.UTF8.GetBytes("x"))));

            await Execute("download");

            File.Exists(Path.Combine(directory.FullName, "mycelium-20260920.log")).Should().BeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenTheClientThrows_WritesTheOperatorMessage()
    {
        _myceliumMock.Setup(c => c.GetLogTailAsync(null, null)).ThrowsAsync(new HttpRequestException("offline"));

        await Execute("tail");

        _writer.ToString().Should().Contain("Error:").And.Contain("offline");
    }

    private static async IAsyncEnumerable<ServerSentEvent> Events(params ServerSentEvent[] events)
    {
        foreach (var one in events)
        {
            await Task.Yield();
            yield return one;
        }
    }

    private static async IAsyncEnumerable<ServerSentEvent> EndlessAfter([EnumeratorCancellation] CancellationToken token, params ServerSentEvent[] events)
    {
        foreach (var one in events)
            yield return one;
        await Task.Delay(Timeout.Infinite, token);
    }
}
