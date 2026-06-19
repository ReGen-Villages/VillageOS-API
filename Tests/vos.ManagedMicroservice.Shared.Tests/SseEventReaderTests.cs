using System.Text;
using FluentAssertions;
using vos.ManagedMicroservice.Shared.Subscriptions;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Tests;

public class SseEventReaderTests
{
    private static async Task<List<SseFrame>> ReadAll(string wire)
    {
        var frames = new List<SseFrame>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(wire));
        await foreach (var f in SseEventReader.ReadAsync(stream))
            frames.Add(f);
        return frames;
    }

    [Fact]
    public async Task Parses_id_event_and_data()
    {
        var frames = await ReadAll("id: 5\nevent: PropertyChanged\ndata: {\"x\":1}\n\n");

        frames.Should().ContainSingle();
        frames[0].Id.Should().Be("5");
        frames[0].EventType.Should().Be("PropertyChanged");
        frames[0].Data.Should().Be("{\"x\":1}");
    }

    [Fact]
    public async Task Ignores_comment_and_heartbeat_lines()
    {
        var frames = await ReadAll(": stream open\n\n:\n\nid: 7\ndata: hi\n\n");

        frames.Should().ContainSingle();
        frames[0].Id.Should().Be("7");
        frames[0].Data.Should().Be("hi");
    }

    [Fact]
    public async Task Joins_multiline_data_with_newlines()
    {
        var frames = await ReadAll("data: line1\ndata: line2\n\n");

        frames[0].Data.Should().Be("line1\nline2");
    }

    [Fact]
    public async Task Parses_multiple_frames_in_order()
    {
        var frames = await ReadAll("id: 1\ndata: a\n\nid: 2\ndata: b\n\n");

        frames.Should().HaveCount(2);
        frames[0].Id.Should().Be("1");
        frames[1].Id.Should().Be("2");
        frames[1].Data.Should().Be("b");
    }

    [Fact]
    public async Task Does_not_emit_a_frame_for_a_trailing_incomplete_block()
    {
        // No blank line terminator -> the partial block is not dispatched.
        var frames = await ReadAll("id: 9\ndata: partial");

        frames.Should().BeEmpty();
    }
}
