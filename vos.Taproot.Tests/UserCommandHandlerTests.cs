using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class UserCommandHandlerTests
{
    private readonly Mock<MyceliumClient> _myceliumMock;
    private readonly StringWriter _writer;

    public UserCommandHandlerTests()
    {
        _myceliumMock = new Mock<MyceliumClient>("https://localhost:7243") { CallBase = false };
        _writer = new StringWriter();
    }

    private async Task ExecuteHandler(string arg, TextReader? reader = null)
    {
        var r = reader ?? new StringReader("");
        var handler = new UserCommandHandler(arg, r, _writer, _myceliumMock.Object);
        await handler.ExecuteAsync();
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>The act the handler posted, as the route would read it.</summary>
    private object? _posted;

    private void AnswerAdministration(string json)
    {
        _myceliumMock
            .Setup(m => m.AdministerAccountsAsync(It.IsAny<object>()))
            .Callback<object>(act => _posted = act)
            .ReturnsAsync(Json(json));
    }

    private JsonElement Posted() => JsonSerializer.SerializeToElement(_posted!);

    [Fact]
    public async Task Execute_NoArgs_ShowsUsage()
    {
        await ExecuteHandler("");

        var output = _writer.ToString();
        Assert.Contains("Usage:", output);
        Assert.Contains("change-password", output);
        Assert.Contains("user list", output);
        Assert.Contains("user create", output);
        Assert.Contains("user grant", output);
    }

    [Fact]
    public async Task List_DrawsOneLinePerAccount_WithRoleAndModels()
    {
        AnswerAdministration("""
            [{"id":"1","name":"admin","role":"admin","models":"every model","password":"set","created":"2026-09-20"},
             {"id":"2","name":"ada","role":"editor","models":"Site A, Site B","password":"must change at next sign-in","created":"2026-09-20"}]
            """);

        await ExecuteHandler("list");

        var output = _writer.ToString();
        Assert.Contains("admin", output);
        Assert.Contains("every model", output);
        Assert.Contains("ada", output);
        Assert.Contains("Site A, Site B", output);
        Assert.Contains("must change at next sign-in", output);
        Assert.Equal("accounts", Posted().GetProperty("view").GetString());
    }

    [Fact]
    public async Task List_WithNoAccounts_SaysSo()
    {
        AnswerAdministration("[]");

        await ExecuteHandler("list");

        Assert.Contains("No accounts", _writer.ToString());
    }

    [Fact]
    public async Task Create_PromptsForTheFirstPassword_AndPostsTheRoleAndTheModel()
    {
        AnswerAdministration("""{"said":"Added ada as editor; they must choose a password at their first sign-in"}""");

        await ExecuteHandler("create ada editor Site A", new StringReader("first-day"));

        var output = _writer.ToString();
        Assert.Contains("First password:", output);
        Assert.Contains("Added ada as editor", output);
        var posted = Posted();
        Assert.Equal("create", posted.GetProperty("view").GetString());
        Assert.Equal("ada", posted.GetProperty("username").GetString());
        Assert.Equal("first-day", posted.GetProperty("password").GetString());
        Assert.Equal("editor", posted.GetProperty("role").GetString());
        Assert.Equal("Site A", posted.GetProperty("models").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task Create_WithoutAModel_PostsNone()
    {
        AnswerAdministration("""{"said":"Added root as admin"}""");

        await ExecuteHandler("create root admin", new StringReader("pw"));

        Assert.False(Posted().TryGetProperty("models", out _));
    }

    [Fact]
    public async Task Create_WithoutARole_ShowsUsage()
    {
        await ExecuteHandler("create ada");

        Assert.Contains("Usage: user create", _writer.ToString());
        _myceliumMock.Verify(m => m.AdministerAccountsAsync(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task Grant_PostsTheAccountAndTheModelByName_WithSpacesKept()
    {
        AnswerAdministration("""{"said":"ada may now enter Site A"}""");

        await ExecuteHandler("grant ada Site A");

        Assert.Contains("ada may now enter Site A", _writer.ToString());
        var posted = Posted();
        Assert.Equal("grant", posted.GetProperty("view").GetString());
        Assert.Equal("ada", posted.GetProperty("record").GetString());
        Assert.Equal("Site A", posted.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Revoke_PostsTheAccountAndTheModel()
    {
        AnswerAdministration("""{"said":"ada may no longer enter Site A"}""");

        await ExecuteHandler("revoke ada Site A");

        Assert.Contains("may no longer enter", _writer.ToString());
        Assert.Equal("revoke", Posted().GetProperty("view").GetString());
    }

    [Fact]
    public async Task Grant_WithoutAModel_ShowsUsage()
    {
        await ExecuteHandler("grant ada");

        Assert.Contains("Usage: user grant", _writer.ToString());
    }

    [Fact]
    public async Task Role_PostsTheNewRole()
    {
        AnswerAdministration("""{"said":"ada is now viewer"}""");

        await ExecuteHandler("role ada viewer");

        Assert.Contains("ada is now viewer", _writer.ToString());
        var posted = Posted();
        Assert.Equal("change-role", posted.GetProperty("view").GetString());
        Assert.Equal("viewer", posted.GetProperty("role").GetString());
    }

    [Fact]
    public async Task ResetPassword_PromptsForTheNewPassword()
    {
        AnswerAdministration("""{"said":"ada signs in with the new password and must then choose their own"}""");

        await ExecuteHandler("reset-password ada", new StringReader("temporary"));

        var output = _writer.ToString();
        Assert.Contains("New password:", output);
        Assert.Contains("must then choose their own", output);
        var posted = Posted();
        Assert.Equal("reset-password", posted.GetProperty("view").GetString());
        Assert.Equal("temporary", posted.GetProperty("password").GetString());
    }

    [Fact]
    public async Task Delete_PostsTheAccount()
    {
        AnswerAdministration("""{"said":"Deleted ada"}""");

        await ExecuteHandler("delete ada");

        Assert.Contains("Deleted ada", _writer.ToString());
        Assert.Equal("delete", Posted().GetProperty("view").GetString());
        Assert.Equal("ada", Posted().GetProperty("record").GetString());
    }

    [Fact]
    public async Task ARefusal_IsShownInTheRoutesOwnWords()
    {
        _myceliumMock
            .Setup(m => m.AdministerAccountsAsync(It.IsAny<object>()))
            .ThrowsAsync(new HttpRequestException("404 (Not Found): There is no account named nobody"));

        await ExecuteHandler("delete nobody");

        var output = _writer.ToString();
        Assert.Contains("Error:", output);
        Assert.Contains("There is no account named nobody", output);
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
    public async Task ChangePassword_ValidGuid_PromptsAndDelegatesToMycelium()
    {
        var userId = Guid.NewGuid();
        var reader = new StringReader("oldPassword\nnewPassword");
        _myceliumMock
            .Setup(b => b.ChangePasswordAsync(userId, "oldPassword", "newPassword"))
            .ReturnsAsync(System.Text.Json.JsonDocument.Parse("{}").RootElement);

        await ExecuteHandler($"change-password {userId}", reader);

        var output = _writer.ToString();
        Assert.Contains("Current password:", output);
        Assert.Contains("New password:", output);
        Assert.Contains("Password changed.", output);
        _myceliumMock.Verify(b => b.ChangePasswordAsync(userId, "oldPassword", "newPassword"), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_MyceliumThrows_ErrorWritten()
    {
        var userId = Guid.NewGuid();
        var reader = new StringReader("old\nnew");
        _myceliumMock
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
        _myceliumMock
            .Setup(b => b.ChangePasswordAsync(userId, "", ""))
            .ReturnsAsync(System.Text.Json.JsonDocument.Parse("{}").RootElement);

        await ExecuteHandler($"change-password {userId}", reader);

        Assert.Contains("Password changed.", _writer.ToString());
    }
}
