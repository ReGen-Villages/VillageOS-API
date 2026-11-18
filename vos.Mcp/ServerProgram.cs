using Microsoft.Extensions.DependencyInjection;
using vos.Mcp.Registry;
using vos.Taproot;

namespace vos.Mcp;

public static class ServerProgram
{
    public const string Name = "vos.Mcp";

    public static MyceliumClient ClientFor(
        string myceliumUrl, string? apiKey, HttpClient? transport = null, Func<DateTime>? clock = null) =>
        transport is null
            ? new MyceliumClient(myceliumUrl, apiKey, Name)
            : new MyceliumClient(myceliumUrl, apiKey, transport, clock, programName: Name);

    public static IMcpServerBuilder AddPlatformServer(this IServiceCollection services, MyceliumClient mycelium) =>
        services
            .AddSingleton(mycelium)
            .AddSingleton<CallerSource>()
            .AddSingleton<ActivityReport>()
            .AddSingleton<PromptRegistry>()
            .AddMcpServer()
            .WithToolsFromAssembly(typeof(ServerProgram).Assembly)
            .ListingOnlyWhatTheCallerMayUse()
            .WithListPromptsHandler((request, cancellationToken) =>
                request.Services!.GetRequiredService<PromptRegistry>().ListAsync(cancellationToken))
            .WithGetPromptHandler((request, cancellationToken) =>
                request.Services!.GetRequiredService<PromptRegistry>().GetAsync(request, cancellationToken));
}
