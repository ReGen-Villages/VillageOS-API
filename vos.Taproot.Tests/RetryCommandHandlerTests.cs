using System.Net;
using FluentAssertions;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class RetryCommandHandlerTests
{
    private static readonly Guid RecordId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Mock<MyceliumClient> _myceliumMock = new("https://localhost:7243") { CallBase = false };
    private readonly StringWriter _writer = new();

    private Task Execute(string arg) => new RetryCommandHandler(arg, _writer, _myceliumMock.Object).ExecuteAsync();

    [Theory]
    [InlineData("")]
    [InlineData("call")]
    [InlineData("record 22222222-2222-2222-2222-222222222222")]
    public async Task Execute_WithoutCallAndARecord_ShowsUsage(string arg)
    {
        await Execute(arg);

        _writer.ToString().Should().Contain("Usage: retry call <recordId> [reason]");
        _myceliumMock.Verify(c => c.RetryCallAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Execute_WithARecordThatIsNotAnIdentifier_SendsNothingAndSaysSo()
    {
        await Execute("call Estimate-Rainfall");

        _writer.ToString().Should().Contain("Error: 'Estimate-Rainfall' is not a record identifier");
        _myceliumMock.Verify(c => c.RetryCallAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Execute_WhenTheBrokerAccepts_PrintsItsAnswer()
    {
        _myceliumMock.Setup(c => c.RetryCallAsync(RecordId, null)).ReturnsAsync("{\"state\":\"Pending\"}");

        await Execute($"call {RecordId}");

        _writer.ToString().Should().Be("{\n  \"state\": \"Pending\"\n}\n");
    }

    [Fact]
    public async Task Execute_SendsTheRestOfTheLineAsTheReason()
    {
        _myceliumMock.Setup(c => c.RetryCallAsync(RecordId, "the rainfall reading is back")).ReturnsAsync("{\"state\":\"Pending\"}");

        await Execute($"call {RecordId} the rainfall reading is back");

        _myceliumMock.Verify(c => c.RetryCallAsync(RecordId, "the rainfall reading is back"), Times.Once);
    }

    [Fact]
    public async Task Execute_WhenTheCallIsNotFailedOrBlocked_SaysSoWithTheBrokersWords()
    {
        _myceliumMock.Setup(c => c.RetryCallAsync(RecordId, null))
            .ThrowsAsync(new HttpRequestException("409 Conflict: {\"error\":\"the record is Sent\"}", null, HttpStatusCode.Conflict));

        await Execute($"call {RecordId}");

        _writer.ToString().Should()
            .Contain("The call is no longer failed or blocked, so it was not retried.")
            .And.Contain("409 Conflict: {\"error\":\"the record is Sent\"}");
    }

    [Fact]
    public async Task Execute_WhenTheAccountMayNotChangeTheModel_SaysSoWithTheBrokersWords()
    {
        _myceliumMock.Setup(c => c.RetryCallAsync(RecordId, null))
            .ThrowsAsync(new HttpRequestException("403 Forbidden", null, HttpStatusCode.Forbidden));

        await Execute($"call {RecordId}");

        _writer.ToString().Should()
            .Contain("This account may not change the model, so the call was not retried.")
            .And.Contain("403 Forbidden");
    }

    [Fact]
    public async Task Execute_WhenTheBrokerRefusesForAnotherReason_WritesItsWords()
    {
        _myceliumMock.Setup(c => c.RetryCallAsync(RecordId, null))
            .ThrowsAsync(new HttpRequestException("404 Not Found: {\"error\":\"no relationship record\"}", null, HttpStatusCode.NotFound));

        await Execute($"call {RecordId}");

        _writer.ToString().Should().Be("Error: 404 Not Found: {\"error\":\"no relationship record\"}\n");
    }
}
