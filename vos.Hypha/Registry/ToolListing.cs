using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using vos.Taproot;

namespace vos.Hypha.Registry;

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
                    if (tools.TryGetValue(tool.Name, out var declared)
                        && await IsAvailableAsync(declared, caller, context.Services!))
                        available.Add(tool);

                listed.Tools = available;
                return listed;
            });

            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                try
                {
                    var caller = await context.Services!.GetRequiredService<CallerSource>().ReadAsync();
                    if (context.MatchedPrimitive is not McpServerTool tool
                        || !await IsAvailableAsync(tool, caller, context.Services!))
                        return Refusal(NotAvailable);

                    return await next(context, cancellationToken);
                }
                catch (Exception refused) when (refused is HttpRequestException or KeyRefusedException)
                {
                    return Refusal(OperatorMessage.For(refused));
                }
            });
        });

    // A tool the registry can read no condition for is offered to nobody, whatever route it arrived by.
    private static async Task<bool> IsAvailableAsync(McpServerTool tool, Caller caller, IServiceProvider services)
    {
        var conditions = tool.Metadata.OfType<ListingConditionAttribute>().ToList();
        foreach (var condition in conditions)
            if (!await condition.HoldsForAsync(caller, services))
                return false;

        return conditions.Count > 0;
    }

    private static CallToolResult Refusal(string reason) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = reason }],
    };
}
