using vos.Service.Metabolism.Configuration;
using vos.Service.Metabolism.Models;
using vos.Service.Metabolism.Services;
using vos.Service.Shared.Middleware;
using Serilog;

namespace vos.Service.Metabolism.Endpoints;

public static class EndpointMapper
{
    public static WebApplication MapMetabolismEndpoints(
        this WebApplication app,
        ResourceDirection direction,
        Func<int> getRequestCount,
        Action incrementRequestCount,
        bool authEnabled = false)
    {
        var mode = direction.LaunchArgument;

        var handleEndpoint = app.MapPost("/handle", (HandleRequest request, HandleRequestProcessor processor) =>
        {
            incrementRequestCount();

            try
            {
                var (entry, error) = processor.ProcessHandle(request);
                if (error != null)
                    return Results.BadRequest(new { error });

                var config = entry!.Config;
                return Results.Ok(new
                {
                    success = true,
                    registered = true,
                    relationshipId = config.RelationshipId,
                    mode,
                    quantity = config.Quantity,
                    unit = config.Unit,
                    frequencySeconds = config.FrequencySeconds,
                    startDelaySeconds = config.StartDelaySeconds,
                    startUtc = config.StartUtc.ToString("o"),
                    endUtc = config.EndUtc.ToString("o"),
                    status = entry.Status
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register simulation for {RelId}", request.RelationshipId);
                return Results.Problem($"Failed to register simulation: {ex.Message}", statusCode: 500);
            }
        });
        handleEndpoint.RequireContract<HandleRequest>();
        if (authEnabled) handleEndpoint.RequireAuthorization();

        app.MapGet("/simulations", (Services.Metabolism engine) =>
        {
            return engine.GetAll().Select(e => new
            {
                relationshipId = e.Config.RelationshipId,
                subjectId = e.Config.SubjectId,
                targetId = e.Config.TargetId,
                quantity = e.Config.Quantity,
                unit = e.Config.Unit,
                propertyPath = e.Config.PropertyPath,
                frequencySeconds = e.Config.FrequencySeconds,
                startDelaySeconds = e.Config.StartDelaySeconds,
                startUtc = e.Config.StartUtc.ToString("o"),
                endUtc = e.Config.EndUtc.ToString("o"),
                status = e.Status,
                tickCount = e.TickCount,
                lastTickUtc = e.LastTickUtc?.ToString("o"),
                lastError = e.LastError,
                registeredAt = e.RegisteredAt.ToString("o")
            });
        });

        app.MapDelete("/simulations/{relationshipId}", (string relationshipId, Services.Metabolism engine) =>
        {
            if (engine.Cancel(relationshipId))
                return Results.Ok(new { message = $"Simulation {relationshipId} cancelled" });
            return Results.NotFound(new { error = $"No simulation found for {relationshipId}" });
        });

        app.MapGet("/health", (Services.Metabolism engine) => new
        {
            status = "Healthy",
            service = $"Metabolism-{mode}",
            requestsProcessed = getRequestCount(),
            activeSimulations = engine.GetAll().Count(e => e.Status == "active"),
            totalSimulations = engine.GetAll().Count(),
            uptime = "active"
        });

        app.MapGet("/stats", (Services.Metabolism engine, MyceliumClient client) => new
        {
            service = $"Metabolism-{mode}",
            version = "2.0.0",
            requestsProcessed = getRequestCount(),
            handlerId = client.HandlerId.ToString(),
            myceliumUrl = client.MyceliumUrl,
            activeSimulations = engine.GetAll().Count(e => e.Status == "active"),
            totalSimulations = engine.GetAll().Count()
        });

        var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime, Services.Metabolism engine) =>
        {
            _ = Task.Run(async () =>
            {
                await engine.StopAllAsync();
                await Task.Delay(300);
                lifetime.StopApplication();
            });

            return new { message = $"Shutting down '{mode}' Metabolism, stopping all simulations" };
        });
        if (authEnabled) shutdownEndpoint.RequireAuthorization();

        return app;
    }
}
