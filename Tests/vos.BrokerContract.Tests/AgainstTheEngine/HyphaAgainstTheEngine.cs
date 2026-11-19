using System.IO.Pipelines;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using vos.Hypha;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

public sealed record ToolAnswer(bool IsError, string Text, JsonElement Structured);

public sealed class HyphaAgainstTheEngine : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly McpClient _chatApplication;

    private HyphaAgainstTheEngine(IHost host, McpClient chatApplication)
    {
        _host = host;
        _chatApplication = chatApplication;
    }

    public static async Task<HyphaAgainstTheEngine> StartAsync(TheEngine engine, string role, params Type[] toolsOfTheTest)
    {
        var made = await engine.Admin.PostAsJsonAsync(
            "/api/auth/keys", new { Name = $"hypha {role} {Guid.NewGuid():N}", Role = role });
        made.EnsureSuccessStatusCode();
        var key = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rawKey").GetString()!;

        var toServer = new Pipe();
        var toChatApplication = new Pipe();
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddPlatformServer(ServerProgram.ClientFor(TheEngine.Url, key, engine.Transport))
            .WithTools(toolsOfTheTest.AsEnumerable())
            .WithStreamServerTransport(toServer.Reader.AsStream(), toChatApplication.Writer.AsStream());

        var host = builder.Build();
        await host.StartAsync();
        var chatApplication = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toChatApplication.Reader.AsStream()));
        return new HyphaAgainstTheEngine(host, chatApplication);
    }

    public async Task<ToolAnswer> CallAsync(string tool, Dictionary<string, object?>? arguments = null)
    {
        var answer = await _chatApplication.CallToolAsync(tool, arguments ?? []);
        return new ToolAnswer(
            answer.IsError == true,
            string.Join("\n", answer.Content.OfType<TextContentBlock>().Select(block => block.Text)),
            JsonSerializer.SerializeToElement(answer.StructuredContent));
    }

    public async ValueTask DisposeAsync()
    {
        await _chatApplication.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
