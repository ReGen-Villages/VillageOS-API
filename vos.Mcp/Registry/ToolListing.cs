using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using vos.Taproot;

namespace vos.Mcp.Registry;

public static class ToolListing
{
    public const string NotAvailable = "This tool is not available to the key this server was given.";

    public static IMcpServerBuilder ListingOnlyWhatTheCallerMayUse(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (context, cancellationToken) =>
            {
                var listed = await next(context, cancellationToken);
                var caller = await context.Services!.GetRequiredService<CallerSource>().ReadAsync();
                var tools = context.Services!.GetServices<McpServerTool>()
                    .ToDictionary(tool => tool.ProtocolTool.Name);

                var available = new List<Tool>();
                foreach (var tool in listed.Tools)
                    if (!tools.TryGetValue(tool.Name, out var declared)
                        || await IsAvailableAsync(declared, caller, context.Services!))
                        available.Add(tool);

                listed.Tools = available;
                return listed;
            });

            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                try
                {
                    var caller = await context.Services!.GetRequiredService<CallerSource>().ReadAsync();
                    if (context.MatchedPrimitive is McpServerTool tool
                        && !await IsAvailableAsync(tool, caller, context.Services!))
                        return Refusal(NotAvailable);

                    return await next(context, cancellationToken);
                }
                catch (Exception refused) when (refused is HttpRequestException or KeyRefusedException)
                {
                    return Refusal(OperatorMessage.For(refused));
                }
            });
        });

    private static async Task<bool> IsAvailableAsync(McpServerTool tool, Caller caller, IServiceProvider services)
    {
        foreach (var condition in tool.Metadata.OfType<ListingConditionAttribute>())
            if (!await condition.HoldsForAsync(caller, services))
                return false;

        return true;
    }

    private static CallToolResult Refusal(string reason) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = reason }],
    };
}
