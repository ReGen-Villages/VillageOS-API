using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class UserCommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private readonly StringWriter _writer;

    public UserCommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg, TextReader? reader = null)
    {
        var r = reader ?? new StringReader("");
        var handler = new UserCommandHandler(arg, r, _writer, _brokerMock.Object);
        await handler.ExecuteAsync();
    }

    [Fact]
    public async Task Execute_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("change-password", output);
    }

    [Fact]
    public async Task Execute_NullArg_ShowsUsage()
    {
        await ExecuteHandler(null!);

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task Execute_UnknownSubcommand_ShowsUsage()
    {
        await ExecuteHandler("unknown");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task ChangePassword_NoUserId_ShowsUsage()
    {
        await ExecuteHandler("change-password");

        Assert.Contains("Usage:", _writer.ToString());
    }

    [Fact]
    public async Task ChangePassword_InvalidGuid_ShowsUsage()
    {
        await ExecuteHandler("change-password not-a-guid");

        var output = _writer.ToString();
        Assert.Contains("Usage: user change-password", output);
    }

    [Fact]
    public async Task ChangePassword_ValidGuid_PromptsAndDelegatesToBroker()
    {
        var userId = Guid.NewGuid();
        var reader = new StringReader("oldPassword\nnewPassword");
        _brokerMock
            .Setup(b => b.ChangePasswordAsync(userId, "oldPassword", "newPassword"))
            .ReturnsAsync(System.Text.Json.JsonDocument.Parse("{}").RootElement);

        await ExecuteHandler($"change-password {userId}", reader);

        var output = _writer.ToString();
        Assert.Contains("Current password:", output);
        Assert.Contains("New password:", output);
        Assert.Contains("Password changed.", output);
        _brokerMock.Verify(b => b.ChangePasswordAsync(userId, "oldPassword", "newPassword"), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_BrokerThrows_ErrorWritten()
    {
        var userId = Guid.NewGuid();
        var reader = new StringReader("old\nnew");
        _brokerMock
            .Setup(b => b.ChangePasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("rejected"));

        await ExecuteHandler($"change-password {userId}", reader);

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("rejected", output);
    }

    [Fact]
    public async Task ChangePassword_ReaderEof_TreatsAsEmptyPassword()
    {
        var userId = Guid.NewGuid();
        var reader = new StringReader(""); // both ReadLine() return null → empty string per implementation
        _brokerMock
            .Setup(b => b.ChangePasswordAsync(userId, "", ""))
            .ReturnsAsync(System.Text.Json.JsonDocument.Parse("{}").RootElement);

        await ExecuteHandler($"change-password {userId}", reader);

        Assert.Contains("Password changed.", _writer.ToString());
    }
}
