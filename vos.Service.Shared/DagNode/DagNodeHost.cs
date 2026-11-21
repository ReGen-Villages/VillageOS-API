using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Hosting;

namespace vos.Service.Shared.DagNode;

public delegate TNode NodeFactory<TNode>(
    IHttpClientFactory httpClientFactory, ILogger<TNode> logger, string myceliumUrl, LaunchToken? launchToken, string? apiKey);

public static class DagNodeHost
{
    public static int Run<TNode>(string[] args, string serviceName, string logFileName, NodeFactory<TNode> createNode)
        where TNode : DagNodeService
    {
        var builder = WebApplication.CreateBuilder(args);

        var launchSettings = ServiceLaunchSettings.Parse(args, builder.Configuration);
        if (launchSettings == null)
        {
            Console.WriteLine(ServiceLaunchSettings.UsageMessage);
            Environment.Exit(1);
            return 1;
        }

        var myceliumUrl = launchSettings.MyceliumUrl;
        var launchToken = builder.Services.AddLaunchToken(launchSettings.Token, myceliumUrl);
        var apiKey = launchSettings.ApiKey;

        ServiceHost.ConfigureLogging(serviceName, logFileName, launchSettings.LogDirectory,
            writeToFile: launchSettings.LogToFile && !builder.Environment.IsEnvironment("Testing"));

        try
        {
            Log.Information("VillageOS {Service} Service — Port: {Port}, Mycelium: {MyceliumUrl}",
                serviceName, launchSettings.Port, myceliumUrl);

            builder.Host.UseSerilog();
            builder.WebHost.UseUrls($"http://localhost:{launchSettings.Port}");
            builder.Services.AddHttpClient();

            var authEnabled = builder.AddMyceliumTokenAuthWhenKeyed(
                launchSettings.VerificationKey, launchSettings.Issuer, launchSettings.Audience);

            builder.Services.AddSingleton(provider => new EndpointServiceMyceliumClient(
                provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<ILogger<EndpointServiceMyceliumClient>>(),
                serviceName, myceliumUrl, launchToken, apiKey: apiKey));
            builder.Services.AddSingleton(provider => createNode(
                provider.GetRequiredService<IHttpClientFactory>(),
                provider.GetRequiredService<ILogger<TNode>>(),
                myceliumUrl, launchToken, apiKey));
            builder.Services.AddMyceliumRegistration(serviceName, launchSettings.Port);

            var app = builder.Build();

            if (authEnabled)
            {
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseMyceliumModelToken();
            }

            var handle = app.MapPost("/handle", async (HttpContext context, TNode node) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());
                return request.Kind == HandleRequestKind.NodeEnvelope
                    ? Results.Ok(await node.HandleNodeAsync(request.Json, context.RequestAborted))
                    : Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedNodeEnvelope(serviceName) });
            });
            if (authEnabled) handle.RequireAuthorization();

            var manifest = app.MapGet("/manifest", (TNode node) => Results.Ok(node.Ports));
            if (authEnabled) manifest.RequireAuthorization();

            app.MapHealthAndStats(serviceName, myceliumUrl);

            var shutdown = app.MapShutdown(serviceName);
            if (authEnabled) shutdown.RequireAuthorization();

            app.Run();
        }
        catch (Exception error)
        {
            return ServiceHost.FailedToStart(serviceName, error);
        }
        finally
        {
            Log.CloseAndFlush();
        }

        return 0;
    }
}
