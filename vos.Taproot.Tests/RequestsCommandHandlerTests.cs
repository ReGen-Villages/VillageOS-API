using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

[Collection(nameof(WorkingDirectoryCollection))]
public class RequestsCommandHandlerTests
{
    private static readonly Guid GaugesConnectionId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid FirstRequestId = Guid.Parse("01999a2b-0000-7000-8000-000000000001");
    private static readonly Guid SecondRequestId = Guid.Parse("01999a2b-0000-7000-8000-000000000002");
    private static readonly Guid SubjectId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private readonly Mock<MyceliumClient> _myceliumMock = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private Task Execute(string arg) => new RequestsCommandHandler(arg, _writer, _myceliumMock.Object).ExecuteAsync();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string Entry(Guid id, int status = 200, long durationMs = 12, string? caller = null, Guid? subject = null) =>
        $$"""
        {"Id":"{{id}}","Time":"2026-10-03T11:15:29.123+00:00","ModelId":"{{Guid.Empty}}",
         "ConnectionId":"{{GaugesConnectionId}}","ConnectionName":"reads the gauges",
         "Caller":{{(caller is null ? "null" : $"\"{caller}\"")}},"SubjectId":{{(subject is null ? "null" : $"\"{subject}\"")}},
         "RelationshipId":null,"Status":{{status}},"DurationMs":{{durationMs}},"BodyBytes":27,"BodyKeptBytes":0}
        """;

    [Fact]
    public async Task Execute_WithoutASubcommand_ShowsUsage()
    {
        await Execute("");
        await Execute("dance");

        _writer.ToString().Should().Contain("Usage:").And.Contain("requests latest").And.Contain("requests follow")
            .And.Contain("requests download").And.Contain("requests show");
    }

    [Fact]
    public async Task Latest_AsksForTheLimitAndConnectionGivenAndWritesALineAnEntry()
    {
        _myceliumMock.Setup(c => c.GetLatestRequestsAsync(2, GaugesConnectionId))
            .ReturnsAsync(Parse($"[{Entry(FirstRequestId)},{Entry(SecondRequestId, status: 0, durationMs: 30000)}]"));

        await Execute($"latest --limit=2 --connection={GaugesConnectionId}");

        _writer.ToString().Should().Be(
            $"2026-10-03 11:15:29Z  200     12 ms  reads the gauges  {FirstRequestId}\n" +
            $"2026-10-03 11:15:29Z    0  30000 ms  reads the gauges  {SecondRequestId}\n");
    }

    [Fact]
    public async Task Latest_ReadsAConnectionNamedByItsName()
    {
        _myceliumMock.Setup(c => c.GetAllThingsAsync())
            .ReturnsAsync(Parse($"[{{\"Id\":\"{GaugesConnectionId}\",\"Name\":\"gauges\"}}]"));
        _myceliumMock.Setup(c => c.GetLatestRequestsAsync(null, GaugesConnectionId)).ReturnsAsync(Parse($"[{Entry(FirstRequestId)}]"));

        await Execute("latest --connection=gauges");

        _writer.ToString().Should().Contain(FirstRequestId.ToString());
    }

    [Fact]
    public async Task Latest_WhenTheLastDayHoldsNothing_SaysSo()
    {
        _myceliumMock.Setup(c => c.GetLatestRequestsAsync(null, null)).ReturnsAsync(Parse("[]"));

        await Execute("latest");

        _writer.ToString().Should().Be("No requests in the last day.\n");
    }

    [Fact]
    public async Task Follow_WritesEachEntryAsItArrivesAndReturnsWhenTheStreamEnds()
    {
        _myceliumMock.Setup(c => c.FollowRequestsAsync(null, GaugesConnectionId, It.IsAny<CancellationToken>()))
            .Returns(Events(
                new ServerSentEvent("request", Entry(FirstRequestId).ReplaceLineEndings("")),
                new ServerSentEvent("Heartbeat", "{}"),
                new ServerSentEvent("request", Entry(SecondRequestId).ReplaceLineEndings(""))));

        await Execute($"follow --for=5 --connection={GaugesConnectionId}");

        var output = _writer.ToString();
        output.Should().Contain(FirstRequestId.ToString()).And.Contain(SecondRequestId.ToString()).And.EndWith("Followed for 5 s.\n");
        output.Split('\n').Should().HaveCount(4, "two entries, the closing line and the empty remainder after it");
    }

    [Fact]
    public async Task Download_SavesTheHoursFileUnderThePlatformsNameOrTheOneGiven()
    {
        var directory = Directory.CreateTempSubdirectory("taproot-requests-");
        try
        {
            _myceliumMock.Setup(c => c.DownloadRequestsAsync("2026100311"))
                .ReturnsAsync(() => new LogDownload("requests-2026100311.jsonl", new MemoryStream(Encoding.UTF8.GetBytes("{}\n"))));
            var named = Path.Combine(directory.FullName, "kept.jsonl");

            await Execute($"download --hour=2026100311 {named}");

            File.ReadAllText(named).Should().Be("{}\n");
            _writer.ToString().Should().Contain($"Requests saved to {named}");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Download_OfAnHourNothingWasRecordedIn_SaysSoAndWritesNoFile()
    {
        _myceliumMock.Setup(c => c.DownloadRequestsAsync("2001010100")).ReturnsAsync((LogDownload?)null);

        await Execute("download --hour=2001010100");

        _writer.ToString().Should().Be("No requests were recorded in hour 2001010100.\n");
    }

    [Fact]
    public async Task Download_WithNoHour_SaysNothingWasRecordedThisHour()
    {
        _myceliumMock.Setup(c => c.DownloadRequestsAsync(null)).ReturnsAsync((LogDownload?)null);

        await Execute("download");

        _writer.ToString().Should().Be("No requests were recorded this hour.\n");
    }

    [Fact]
    public async Task Show_WritesTheEntryInFull()
    {
        _myceliumMock.Setup(c => c.GetRequestAsync(FirstRequestId))
            .ReturnsAsync(Parse(Entry(FirstRequestId, caller: "mara", subject: SubjectId)));

        await Execute($"show {FirstRequestId}");

        _writer.ToString().Should().Be(
            $"Request {FirstRequestId}\n" +
            "  Time:          2026-10-03 11:15:29Z\n" +
            $"  Connection:    reads the gauges ({GaugesConnectionId})\n" +
            "  Caller:        mara\n" +
            $"  Subject:       {SubjectId}\n" +
            "  Answered:      200 after 12 ms\n" +
            "  Body:          27 bytes, none kept\n");
    }

    [Fact]
    public async Task Show_OfAnEntryNothingAnswered_SaysSo()
    {
        _myceliumMock.Setup(c => c.GetRequestAsync(FirstRequestId)).ReturnsAsync(Parse(Entry(FirstRequestId, status: 0)));

        await Execute($"show {FirstRequestId}");

        _writer.ToString().Should().Contain("  Answered:      nothing answered after 12 ms\n");
    }

    [Fact]
    public async Task Show_OfAnEntryNotShown_SaysSo()
    {
        _myceliumMock.Setup(c => c.GetRequestAsync(FirstRequestId)).ReturnsAsync((JsonElement?)null);

        await Execute($"show {FirstRequestId}");

        _writer.ToString().Should().Be($"No request {FirstRequestId} is in the log you may read.\n");
    }

    [Fact]
    public async Task Show_OfAnEntryPastTheRetentionPeriod_SaysItIsNoLongerKept()
    {
        _myceliumMock.Setup(c => c.GetRequestAsync(FirstRequestId))
            .ThrowsAsync(new HttpRequestException("410 Gone", null, HttpStatusCode.Gone));

        await Execute($"show {FirstRequestId}");

        _writer.ToString().Should().Be(
            $"Request {FirstRequestId} is no longer kept: it was made before the period the request log keeps.\n");
    }

    [Fact]
    public async Task Show_WithoutAnIdentifier_ShowsItsUsage()
    {
        await Execute("show gauges");

        _writer.ToString().Should().Be("Usage: requests show <request-id>\n");
        _myceliumMock.Verify(c => c.GetRequestAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task A_refusal_from_the_platform_is_written_as_an_error()
    {
        _myceliumMock.Setup(c => c.GetLatestRequestsAsync(null, null))
            .ThrowsAsync(new HttpRequestException("403 Forbidden", null, HttpStatusCode.Forbidden));

        await Execute("latest");

        _writer.ToString().Should().StartWith("Error: ");
    }

    private static async IAsyncEnumerable<ServerSentEvent> Events(params ServerSentEvent[] events)
    {
        foreach (var received in events)
        {
            await Task.Yield();
            yield return received;
        }
    }
}
