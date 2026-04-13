using System.Text.Json;
using vos.Auth.Shared;
using vos.ManagedMicroservice.IntegrationRegistry.Configuration;
using vos.ManagedMicroservice.IntegrationRegistry.Models;
using vos.ManagedMicroservice.IntegrationRegistry.Services;
using vos.Microservice.Shared.Validation;
using Serilog;

var cliArgs = CliArgs.Parse(args);
if (cliArgs == null)
{
    Console.WriteLine(CliArgs.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = cliArgs.Port;
var brokerUrl = cliArgs.BrokerUrl;
var serviceToken = cliArgs.Token;
var signingKey = cliArgs.SigningKey;

// Configure Serilog before building the host.
var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "integrationregistry-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "IntegrationRegistry")
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

try
{
    Log.Information("VillageOS IntegrationRegistry Service - Port: {Port}, Broker: {BrokerUrl}", servicePort, brokerUrl);

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    // Add JWT auth if broker provided a signing key.
    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddBrokerTokenAuth(signingKey!);
        Log.Information("JWT authentication enabled for incoming broker requests");
    }

    builder.Services.AddSingleton(sp =>
        new BrokerClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<BrokerClient>>(),
            brokerUrl,
            serviceToken));

    var app = builder.Build();
    var endpointSeed = LoadEndpointSeed(app.Services.GetRequiredService<ILogger<BrokerClient>>());

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Endpoint-service entry point used by broker /api/endpoints/{subdomain}.
    var handleEndpoint = app.MapPost("/handle", async (RegisterEndpointRequest request, BrokerClient brokerClient) =>
    {
        return await HandleRegisterEndpointRequestAsync(request, brokerClient, endpointSeed);
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    // Backward-compatible alias that uses the same registration logic.
    var registerEndpoint = app.MapPost("/register", async (RegisterEndpointRequest request, BrokerClient brokerClient) =>
    {
        return await HandleRegisterEndpointRequestAsync(request, brokerClient, endpointSeed);
    });
    if (authEnabled) registerEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "IntegrationRegistry-CSharp" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down IntegrationRegistry" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "IntegrationRegistry terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static async Task<IResult> HandleRegisterEndpointRequestAsync(
    RegisterEndpointRequest request,
    BrokerClient brokerClient,
    RegisterEndpointRequest endpointSeed)
{
    BrokerClient.BrokerThing? registeredThing = null;
    try
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Request must include a non-empty thing name." });

        if (request.Properties == null)
            return Results.BadRequest(new { error = "Request must include properties." });

        var isPredicate = await brokerClient.FindThingByNameAsync("is");
        if (isPredicate == null)
        {
            return Results.Problem(
                detail: "Missing required 'is' predicate thing in broker model.",
                statusCode: 500,
                title: "Registration failed");
        }

        var allowedKeys = endpointSeed.Properties?.Keys?.ToArray() ?? Array.Empty<string>();
        if (allowedKeys.Length == 0)
        {
            return Results.Problem(
                detail: "Endpoint seed does not define any allowed properties.",
                statusCode: 500,
                title: "Registration failed");
        }

        var allowedSet = new HashSet<string>(allowedKeys, StringComparer.OrdinalIgnoreCase);
        var invalidKeys = request.Properties.Keys.Where(k => !allowedSet.Contains(k)).ToList();
        if (invalidKeys.Count > 0)
        {
            return Results.BadRequest(new
            {
                error = $"Endpoint thing has unsupported properties: {string.Join(", ", invalidKeys)}"
            });
        }

        if (!TryGetStringProperty(request.Properties, "url", out var url) || string.IsNullOrWhiteSpace(url))
            return Results.BadRequest(new { error = "Endpoint url must be a non-empty string." });

        if (!TryGetStringProperty(request.Properties, "httpMethod", out var method) || string.IsNullOrWhiteSpace(method))
            return Results.BadRequest(new { error = "Endpoint httpMethod must be a non-empty string." });

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return Results.BadRequest(new { error = $"Invalid endpoint url: {url}" });

        var normalizedMethod = method.Trim().ToUpperInvariant();
        if (!HttpMethodValidator.IsSupportedMethod(normalizedMethod))
            return Results.BadRequest(new { error = $"Unsupported httpMethod: {method}" });

        var defaultEndpoint = await brokerClient.FindThingByNameAsync(endpointSeed.Name);
        if (defaultEndpoint == null)
        {
            defaultEndpoint = await brokerClient.CreateThingAsync(new RegisterEndpointRequest
            {
                Name = endpointSeed.Name,
                Properties = endpointSeed.Properties ?? new Dictionary<string, object>()
            });
        }

        if (defaultEndpoint == null)
        {
            return Results.Problem(
                detail: "Could not resolve default Endpoint thing in broker.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Create thing with name only - no own properties.
        registeredThing = await brokerClient.CreateThingAsync(new RegisterEndpointRequest
        {
            Name = request.Name
        });

        if (registeredThing == null)
        {
            return Results.Problem(
                detail: "Failed to create registered endpoint thing.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Create 'is' relationship. The broker awaits the is-handler synchronously,
        // so inherited properties are available before this call returns.
        var relationshipCreated = await brokerClient.CreateRelationshipAsync(
            registeredThing.Value.Id,
            isPredicate.Value.Id,
            defaultEndpoint.Value.Id);

        if (!relationshipCreated)
        {
            await CompensateAsync(brokerClient, registeredThing.Value.Id);
            return Results.Problem(
                detail: "Failed to create 'is' relationship for registered endpoint.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Set user-supplied values on inherited properties.
        foreach (var (propName, propValue) in request.Properties)
        {
            var set = await brokerClient.SetThingPropertyAsync(registeredThing.Value.Id, propName, propValue);
            if (!set)
            {
                await CompensateAsync(brokerClient, registeredThing.Value.Id);
                return Results.Problem(
                    detail: $"Failed to set property '{propName}' on registered endpoint.",
                    statusCode: 500,
                    title: "Registration failed");
            }
        }

        return Results.Ok(new
        {
            success = true,
            message = "Endpoint registered successfully",
            registeredThingId = registeredThing.Value.Id,
            endpointTemplateId = defaultEndpoint.Value.Id,
            predicateId = isPredicate.Value.Id
        });
    }
    catch (Exception ex)
    {
        if (registeredThing != null)
            await CompensateAsync(brokerClient, registeredThing.Value.Id);

        Log.Error(ex, "Error registering endpoint {Name}", request.Name);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Failed to register endpoint");
    }
}

static async Task CompensateAsync(BrokerClient brokerClient, Guid thingId)
{
    Log.Warning("Compensating: deleting orphaned thing {ThingId}", thingId);
    var deleted = await brokerClient.DeleteThingAsync(thingId);
    if (!deleted)
        Log.Error("Compensation failed: could not delete orphaned thing {ThingId}", thingId);
}

static RegisterEndpointRequest LoadEndpointSeed(Microsoft.Extensions.Logging.ILogger logger)
{
    var candidatePaths = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "seed.json"),
        Path.Combine(Directory.GetCurrentDirectory(), "seed.json"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "seed.json")
    };

    foreach (var candidate in candidatePaths.Select(Path.GetFullPath))
    {
        if (!File.Exists(candidate))
            continue;

        try
        {
            var content = File.ReadAllText(candidate);
            var seed = JsonSerializer.Deserialize<RegisterEndpointRequest>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (seed != null && !string.IsNullOrWhiteSpace(seed.Name))
                return seed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse seed file {Path}", candidate);
        }
    }

    throw new InvalidOperationException("Could not load a valid Endpoint seed from seed.json.");
}

static bool TryGetStringProperty(Dictionary<string, object> properties, string name, out string? value)
{
    value = null;
    if (!TryGetPropertyValue(properties, name, out var raw))
        return false;

    value = CoerceToString(raw);
    return true;
}

static bool TryGetPropertyValue(Dictionary<string, object> properties, string name, out object? value)
{
    if (properties.TryGetValue(name, out value))
        return true;

    foreach (var entry in properties)
    {
        if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
        {
            value = entry.Value;
            return true;
        }
    }

    value = null;
    return false;
}

static string? CoerceToString(object? value)
{
    if (value is JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    return value?.ToString();
}
