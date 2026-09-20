using FluentAssertions;
using Xunit;

namespace vos.Taproot.Tests;

public class ServerSentEventsTests
{
    private static async Task<List<ServerSentEvent>> Read(string text)
    {
        var received = new List<ServerSentEvent>();
        await foreach (var one in ServerSentEvents.ReadAsync(new StringReader(text)))
            received.Add(one);
        return received;
    }

    [Fact]
    public async Task Read_YieldsEachEventNameAndDataAndSkipsCommentsAndHeartbeats()
    {
        const string stream = ": stream open\n\n"
            + "event: log\ndata: \"first line\"\n\n"
            + ":\n\n"
            + "event: ThingCreated\ndata: {\"Id\":\"a\"}\n\n";

        var received = await Read(stream);

        received.Should().Equal(
            new ServerSentEvent("log", "\"first line\""),
            new ServerSentEvent("ThingCreated", "{\"Id\":\"a\"}"));
    }

    [Fact]
    public async Task Read_JoinsSeveralDataLinesWithNewlinesAndNamesAnUnnamedEventMessage()
    {
        var received = await Read("data: one\ndata: two\n\n");

        received.Should().ContainSingle().Which.Should().Be(new ServerSentEvent("message", "one\ntwo"));
    }

    [Fact]
    public async Task Read_IgnoresAnEventNameWithNoDataAndAFieldItDoesNotKnow()
    {
        var received = await Read("event: nothing\n\nid: 7\nretry: 100\ndata:bare\n\n");

        received.Should().ContainSingle().Which.Should().Be(new ServerSentEvent("message", "bare"));
    }

    [Fact]
    public async Task Read_StopsWhenTheCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new BlockingReader("event: log\ndata: \"one\"\n\n", cancellation);
        var received = new List<ServerSentEvent>();

        var act = async () =>
        {
            await foreach (var one in ServerSentEvents.ReadAsync(reader, cancellation.Token))
                received.Add(one);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        received.Should().ContainSingle();
    }

    // Hands over its lines, then cancels and waits like a stream with nothing more to say.
    private sealed class BlockingReader : TextReader
    {
        private readonly Queue<string> _lines;
        private readonly CancellationTokenSource _cancellation;

        public BlockingReader(string text, CancellationTokenSource cancellation)
        {
            _lines = new Queue<string>(text.Split('\n'));
            _cancellation = cancellation;
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (_lines.Count > 1) return _lines.Dequeue();
            _cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }
}
