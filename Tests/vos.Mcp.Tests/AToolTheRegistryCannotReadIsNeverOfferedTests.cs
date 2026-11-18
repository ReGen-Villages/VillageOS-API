using System.ComponentModel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;
using static vos.Mcp.Tests.ServerUnderTest;

namespace vos.Mcp.Tests;

public class AToolTheRegistryCannotReadIsNeverOfferedTests
{
    private bool _ran;

    // A tool served by a handler has no method for the registry to read conditions from.
    private void AToolServedByAHandler(IServiceCollection services) =>
        services.AddMcpServer()
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult
            {
                Tools = [new Tool { Name = "served_by_a_handler" }],
            }))
            .WithCallToolHandler((_, _) =>
            {
                _ran = true;
                return ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = "ran" }] });
            });

    [Fact]
    public async Task A_tool_with_no_declaration_to_read_is_not_listed()
    {
        await using var server = await StartAsync(MyceliumFor(() => "admin"), configure: AToolServedByAHandler);

        (await server.ListedToolNamesAsync()).Should().BeEquivalentTo(["list_models"]);
    }

    [Fact]
    public async Task A_call_to_a_tool_with_no_declaration_to_read_is_refused_and_the_tool_does_not_run()
    {
        await using var server = await StartAsync(MyceliumFor(() => "admin"), configure: AToolServedByAHandler);

        var refused = await server.Client.CallToolAsync("served_by_a_handler");

        refused.IsError.Should().BeTrue();
        TextOf(refused).Should().Be(Registry.ToolListing.NotAvailable);
        _ran.Should().BeFalse();
    }

    [Fact]
    public async Task A_tool_that_declares_no_condition_is_not_listed_and_a_call_to_it_is_refused()
    {
        await using var server = await StartAsync(MyceliumFor(() => "admin"), toolTypes: typeof(AToolWithNoCondition));

        (await server.ListedToolNamesAsync()).Should().NotContain("sample_unconditioned");

        var refused = await server.Client.CallToolAsync("sample_unconditioned");
        refused.IsError.Should().BeTrue();
        TextOf(refused).Should().Be(Registry.ToolListing.NotAvailable);
    }

    [McpServerToolType]
    public sealed class AToolWithNoCondition
    {
        [McpServerTool(Name = "sample_unconditioned", ReadOnly = true)]
        [Description("Declares nothing about who may use it.")]
        public static string Run() => "ran";
    }
}
