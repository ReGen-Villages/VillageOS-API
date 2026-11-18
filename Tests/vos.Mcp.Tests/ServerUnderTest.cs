using System.IO.Pipelines;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using vos.Taproot;
using vos.Tests.Shared;

namespace vos.Mcp.Tests;

public sealed class ServerUnderTest : IAsyncDisposable
{
    public const string MyceliumUrl = "https://localhost:7243";
    public const string ApiKey = "a-key-that-must-never-be-shown";

    private readonly IHost _host;

    private readonly RecordingStream _sentToClient;

    private ServerUnderTest(IHost host, McpClient client, MockHttpMessageHandler mycelium, RecordingStream sentToClient)
    {
        _host = host;
        _sentToClient = sentToClient;
        Client = client;
        Mycelium = mycelium;
    }

    public McpClient Client { get; }

    public MockHttpMessageHandler Mycelium { get; }

    public static async Task<ServerUnderTest> StartAsync(
        Func<HttpRequestMessage, HttpResponseMessage> mycelium, McpClientOptions? chatApplication = null,
        Func<DateTime>? clock = null, Action<IServiceCollection>? configure = null, params Type[] toolTypes)
    {
        var handler = new MockHttpMessageHandler(mycelium);
        var toServer = new Pipe();
        var toClient = new Pipe();
        var sentToClient = new RecordingStream(toClient.Writer.AsStream());

        var builder = Host.CreateApplicationBuilder();
        configure?.Invoke(builder.Services);
        builder.Services
            .AddPlatformServer(new MyceliumClient(
                MyceliumUrl, ApiKey, new HttpClient(handler), clock, programName: ServerProgram.Name))
            .WithTools(toolTypes.AsEnumerable())
            .WithStreamServerTransport(toServer.Reader.AsStream(), sentToClient);

        var host = builder.Build();
        await host.StartAsync();

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), chatApplication);
        return new ServerUnderTest(host, client, handler, sentToClient);
    }

    public string SentToTheChatApplication => _sentToClient.Written;

    public static Func<HttpRequestMessage, HttpResponseMessage> MyceliumFor(
        Func<string> role, Func<HttpRequestMessage, HttpResponseMessage>? otherwise = null) => request =>
        request.RequestUri!.AbsolutePath == "/api/auth/token"
            ? Json($$"""{"token":"{{TokenFor(role())}}"}""")
            : otherwise?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);

    public static string TokenFor(string role) =>
        TestTokens.Jwt(new Dictionary<string, object> { [ClaimTypes.Role] = role });

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public async Task<IReadOnlyList<string>> ListedToolNamesAsync() =>
        (await Client.ListToolsAsync()).Select(tool => tool.Name).ToList();

    public static string TextOf(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
