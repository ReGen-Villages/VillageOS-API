using System.IO.Pipelines;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using vos.Hypha;
using vos.PlatformClient;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The server decides what to list from its own table of which role satisfies which policy. The engine holds
// the real table, so each role's list is called against it and a tool the engine forbids fails here.
public class EveryToolAKeyIsListedIsOneMyceliumLetsItCallTests : IClassFixture<TheEngine>
{
    private readonly TheEngine _engine;

    public EveryToolAKeyIsListedIsOneMyceliumLetsItCallTests(TheEngine engine) => _engine = engine;

    [FactAgainstTheEngine]
    public Task No_tool_listed_for_a_viewer_is_refused_for_want_of_a_policy() => NoListedToolIsForbiddenAsync("viewer");

    [FactAgainstTheEngine]
    public Task No_tool_listed_for_an_editor_is_refused_for_want_of_a_policy() => NoListedToolIsForbiddenAsync("editor");

    [FactAgainstTheEngine]
    public Task No_tool_listed_for_an_administrator_is_refused_for_want_of_a_policy() => NoListedToolIsForbiddenAsync("admin");

    private async Task NoListedToolIsForbiddenAsync(string role)
    {
        var made = await _engine.Admin.PostAsJsonAsync(
            "/api/auth/keys", new { Name = $"listing check {role} {Guid.NewGuid():N}", Role = role });
        made.EnsureSuccessStatusCode();
        var key = (await made.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rawKey").GetString()!;

        // Never disposed, because disposing it would dispose the engine's transport, which every case shares.
        var answered = new StatusesTheEngineAnswered(_engine.Transport);
        var toServer = new Pipe();
        var toChatApplication = new Pipe();
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddPlatformServer(ServerProgram.ClientFor(
                TheEngine.Url, key, answered))
            .WithStreamServerTransport(toServer.Reader.AsStream(), toChatApplication.Writer.AsStream());

        using var host = builder.Build();
        await host.StartAsync();
        await using var chatApplication = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toChatApplication.Reader.AsStream()));

        var tools = await chatApplication.ListToolsAsync();
        tools.Should().NotBeEmpty("a key of every role is listed at least the tools that read");

        foreach (var tool in tools)
        {
            answered.Forget();
            await chatApplication.CallToolAsync(tool.Name, ArgumentsFor(tool.ProtocolTool));

            answered.RefusedForWantOfAPolicy.Should().BeFalse("{0} is listed for a key whose role is {1}", tool.Name, role);
        }

        await host.StopAsync();
    }

    // The arguments only have to reach the engine, which may answer that it holds no such Thing.
    private static Dictionary<string, object?> ArgumentsFor(Tool tool)
    {
        var arguments = new Dictionary<string, object?>();
        if (!tool.InputSchema.TryGetProperty("required", out var required)) return arguments;

        var properties = tool.InputSchema.GetProperty("properties");
        foreach (var name in required.EnumerateArray().Select(one => one.GetString()!))
        {
            var declared = properties.GetProperty(name);
            var type = declared.TryGetProperty("type", out var stated) && stated.ValueKind == JsonValueKind.String
                ? stated.GetString()
                : "string";
            var isIdentifier = declared.TryGetProperty("format", out var format) && format.GetString() == "uuid";

            arguments[name] = type switch
            {
                "integer" or "number" => 1,
                "boolean" => false,
                "array" => Array.Empty<string>(),
                "object" => new Dictionary<string, object?>(),
                _ => isIdentifier ? Guid.NewGuid().ToString() : "a",
            };
        }

        return arguments;
    }
}
