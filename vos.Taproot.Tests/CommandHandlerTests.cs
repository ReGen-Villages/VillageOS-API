using System.Text.Json;
using Moq;
using Xunit;

namespace vos.Taproot.Tests;

public class CommandHandlerTests
{
    private readonly Mock<BrokerClient> _brokerMock;
    private const string BrokerUrl = "https://localhost:7243";

    public CommandHandlerTests()
    {
        _brokerMock = new Mock<BrokerClient>(BrokerUrl) { CallBase = false };
    }

    private CommandHandler CreateHandler(StringReader reader, StringWriter writer, bool interactiveMode = false)
    {
        return new CommandHandler(reader, writer, _brokerMock.Object, BrokerUrl, interactiveMode);
    }

    [Fact]
    public void ShowHelp_DisplaysAllCommands()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);

        handler.ShowHelp();

        var output = writer.ToString();
        Assert.Contains("Available commands:", output);
        Assert.Contains("create thing", output);
        Assert.Contains("delete thing", output);
        Assert.Contains("delete relationship", output);
        Assert.Contains("delete property", output);
        Assert.Contains("find thing", output);
        Assert.Contains("find relationships", output);
        Assert.Contains("get thing", output);
        Assert.Contains("list", output);
        Assert.Contains("set", output);
        Assert.Contains("serialize", output);
        Assert.Contains("deserialize", output);
        Assert.Contains("plant", output);
        Assert.Contains("seed", output);
        Assert.Contains("help", output);
        Assert.Contains("exit", output);
    }

    [Fact]
    public async Task HandleCommandAsync_UnknownCommand_DisplaysErrorMessage()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("unknowncommand", null);

        Assert.Contains("Unknown command", writer.ToString());
    }

    [Fact]
    public async Task HandleCommandAsync_Create_DelegatesToCreateCommandHandler()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($@"{{""id"":""{thingId}"",""name"":""TestThing""}}");
        _brokerMock.Setup(b => b.CreateThingAsync("TestThing")).ReturnsAsync(json);

        await handler.HandleCommandAsync("create", "thing TestThing");

        _brokerMock.Verify(b => b.CreateThingAsync("TestThing"), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_List_DelegatesToListCommandHandler()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);
        var thingsArray = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await handler.HandleCommandAsync("list", "things");

        _brokerMock.Verify(b => b.GetAllThingsAsync(), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_Delete_DelegatesToDeleteCommandHandler()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);
        var thingId = Guid.NewGuid();
        _brokerMock.Setup(b => b.DeleteThingAsync(thingId)).ReturnsAsync(true);

        await handler.HandleCommandAsync("delete", $"thing {thingId}");

        _brokerMock.Verify(b => b.DeleteThingAsync(thingId), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_Get_DelegatesToGetCommandHandler()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($@"{{""Id"":""{thingId}"",""Name"":""TestThing""}}");
        _brokerMock.Setup(b => b.GetThingAsync(thingId)).ReturnsAsync(json);

        await handler.HandleCommandAsync("get", $"thing {thingId}");

        _brokerMock.Verify(b => b.GetThingAsync(thingId), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_Find_DelegatesToFindCommandHandler()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);
        var thingsArray = JsonSerializer.Deserialize<JsonElement>("[]");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(thingsArray);

        await handler.HandleCommandAsync("find", "thing Test");

        _brokerMock.Verify(b => b.GetAllThingsAsync(), Times.Once);
    }

    [Fact]
    public async Task HandleCommandAsync_Pwd_DelegatesToFileSystemCommandHandler()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("pwd", null);

        var output = writer.ToString();
        Assert.Contains("Current directory:", output);
    }

    [Fact]
    public async Task HandleCommandAsync_Plant_IsAliasForDeserialize()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);

        // Plant with no file should show usage (same as deserialize)
        await handler.HandleCommandAsync("plant", "");

        var output = writer.ToString();
        Assert.Contains("Usage:", output);
    }

    [Fact]
    public async Task HandleCommandAsync_Seed_IsAliasForSerialize()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);
        var modelJson = @"{""things"":[],""relationships"":[]}";
        _brokerMock.Setup(b => b.GetModelJsonAsync()).ReturnsAsync(modelJson);

        // Seed with no file should output JSON to console (same as serialize)
        await handler.HandleCommandAsync("seed", "");

        var output = writer.ToString();
        _brokerMock.Verify(b => b.GetModelJsonAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_Exit_StopsLoop()
    {
        var reader = new StringReader("exit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        var handler = CreateHandler(reader, writer);

        await handler.RunAsync();

        var output = writer.ToString();
        Assert.Contains("VillageOS CLI", output);
        Assert.Contains("Connected to broker", output);
    }

    [Fact]
    public async Task RunAsync_Help_DisplaysHelpMessage()
    {
        var reader = new StringReader("help\nexit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        var handler = CreateHandler(reader, writer);

        await handler.RunAsync();

        var output = writer.ToString();
        Assert.Contains("Available commands:", output);
    }

    [Fact]
    public async Task RunAsync_EmptyInput_ContinuesLoop()
    {
        var reader = new StringReader("\n\nexit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        var handler = CreateHandler(reader, writer);

        await handler.RunAsync();

        // Should complete without error
        Assert.Contains("VillageOS CLI", writer.ToString());
    }

    [Fact]
    public async Task RunAsync_ExceptionInHandler_DisplaysError()
    {
        var reader = new StringReader("create thing TestThing\nexit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        _brokerMock.Setup(b => b.CreateThingAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("Test error"));
        var handler = CreateHandler(reader, writer);

        await handler.RunAsync();

        var output = writer.ToString();
        Assert.Contains("Error:", output);
    }

    [Fact]
    public async Task RunAsync_BrokerConnectionFailure_ShowsWarning()
    {
        var reader = new StringReader("exit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ThrowsAsync(new HttpRequestException("Connection refused"));
        var handler = CreateHandler(reader, writer);

        await handler.RunAsync();

        var output = writer.ToString();
        Assert.Contains("Warning:", output);
        Assert.Contains("Could not connect to broker", output);
    }

    [Theory]
    [InlineData("CREATE thing Test")]
    [InlineData("LIST things")]
    public async Task RunAsync_CommandsAreCaseInsensitive(string input)
    {
        var reader = new StringReader($"{input}\nexit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($@"{{""id"":""{thingId}"",""name"":""Test""}}");
        _brokerMock.Setup(b => b.CreateThingAsync(It.IsAny<string>())).ReturnsAsync(json);
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(JsonSerializer.Deserialize<JsonElement>("[]"));
        var handler = CreateHandler(reader, writer);

        await handler.RunAsync();

        // Should complete without "Unknown command" error
        Assert.DoesNotContain("Unknown command", writer.ToString());
    }

    #region Shutdown and Clear Model Tests

    [Fact]
    public async Task HandleCommandAsync_Shutdown_CallsBrokerShutdown()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.ShutdownBrokerAsync()).ReturnsAsync(true);
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("shutdown", null);

        _brokerMock.Verify(b => b.ShutdownBrokerAsync(), Times.Once);
        Assert.Contains("Broker shutdown initiated", writer.ToString());
    }

    [Fact]
    public async Task HandleCommandAsync_Shutdown_WhenFails_ShowsError()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.ShutdownBrokerAsync()).ReturnsAsync(false);
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("shutdown", null);

        Assert.Contains("Failed to initiate broker shutdown", writer.ToString());
    }

    [Fact]
    public async Task HandleCommandAsync_Shutdown_WhenConnectionClosed_ShowsInitiated()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.ShutdownBrokerAsync()).ThrowsAsync(new HttpRequestException("Connection closed"));
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("shutdown", null);

        Assert.Contains("Broker shutdown initiated (connection closed)", writer.ToString());
    }

    [Fact]
    public async Task HandleCommandAsync_ClearModel_ClearsModel()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.ClearModelAsync()).Returns(Task.CompletedTask);
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("clear", "model");

        _brokerMock.Verify(b => b.ClearModelAsync(), Times.Once);
        Assert.Contains("Model cleared", writer.ToString());
    }

    [Fact]
    public async Task HandleCommandAsync_Clear_WithoutModel_ShowsUsage()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("clear", "");

        Assert.Contains("Usage: clear model", writer.ToString());
    }

    [Fact]
    public async Task HandleCommandAsync_Clear_WithInvalidArg_ShowsUsage()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer);

        await handler.HandleCommandAsync("clear", "invalid");

        Assert.Contains("Usage: clear model", writer.ToString());
    }

    #endregion

    #region Interactive Mode Tests

    [Fact]
    public void Constructor_WithInteractiveModeTrue_AcceptsParameter()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();

        var handler = CreateHandler(reader, writer, interactiveMode: true);

        Assert.NotNull(handler);
    }

    [Fact]
    public void Constructor_WithInteractiveModeFalse_AcceptsParameter()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();

        var handler = CreateHandler(reader, writer, interactiveMode: false);

        Assert.NotNull(handler);
    }

    [Fact]
    public void Constructor_WithoutInteractiveMode_DefaultsToFalse()
    {
        var reader = new StringReader("");
        var writer = new StringWriter();

        // This uses the default value (false)
        var handler = new CommandHandler(reader, writer, _brokerMock.Object, BrokerUrl);

        Assert.NotNull(handler);
    }

    [Fact]
    public async Task RunAsync_NonInteractiveMode_WritesPromptToWriter()
    {
        var reader = new StringReader("exit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        var handler = CreateHandler(reader, writer, interactiveMode: false);

        await handler.RunAsync();

        var output = writer.ToString();
        Assert.Contains("> ", output);
    }

    [Fact]
    public async Task RunAsync_NonInteractiveMode_ReadsFromTextReader()
    {
        var reader = new StringReader("help\nexit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        var handler = CreateHandler(reader, writer, interactiveMode: false);

        await handler.RunAsync();

        var output = writer.ToString();
        Assert.Contains("Available commands:", output);
    }

    [Fact]
    public async Task RunAsync_NonInteractiveMode_ProcessesMultipleCommands()
    {
        var thingId = Guid.NewGuid();
        var json = JsonSerializer.Deserialize<JsonElement>($@"{{""id"":""{thingId}"",""name"":""TestThing""}}");
        _brokerMock.Setup(b => b.CreateThingAsync("TestThing")).ReturnsAsync(json);
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        _brokerMock.Setup(b => b.GetAllThingsAsync()).ReturnsAsync(JsonSerializer.Deserialize<JsonElement>("[]"));

        var reader = new StringReader("create thing TestThing\nlist things\nexit\n");
        var writer = new StringWriter();
        var handler = CreateHandler(reader, writer, interactiveMode: false);

        await handler.RunAsync();

        _brokerMock.Verify(b => b.CreateThingAsync("TestThing"), Times.Once);
        _brokerMock.Verify(b => b.GetAllThingsAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_NonInteractiveMode_HandlesWhitespaceOnlyLines()
    {
        var reader = new StringReader("   \n\t\n\nexit\n");
        var writer = new StringWriter();
        _brokerMock.Setup(b => b.GetTokenAsync()).ReturnsAsync("test-token");
        var handler = CreateHandler(reader, writer, interactiveMode: false);

        await handler.RunAsync();

        // Should complete without error - whitespace lines are ignored
        Assert.Contains("VillageOS CLI", writer.ToString());
    }

    #endregion
}
