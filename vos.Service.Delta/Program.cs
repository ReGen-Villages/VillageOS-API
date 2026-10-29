using System.Text.Json;
using vos.Auth.Shared;
using vos.Service.Delta.Helpers;
using vos.Service.Delta.Models;
using vos.Service.Delta.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Validation;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

var launchSettings = ServiceLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ServiceLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return 1;
}

var servicePort = launchSettings.Port;
var myceliumUrl = launchSettings.MyceliumUrl;
var serviceToken = launchSettings.Token;
var apiKey = launchSettings.ApiKey;
var verificationKey = launchSettings.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Delta", "delta-.log", launchSettings.LogDirectory, writeToFile: !isTestingEnv);

try
{
    Log.Information("VillageOS Delta Service - Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    // Issuer/audience must match what Mycelium signed, hence taken from CLI.
    var authEnabled = !string.IsNullOrEmpty(verificationKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(
            verificationKey!,
            issuer: launchSettings.Issuer,
            audience: launchSettings.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            launchSettings.Issuer, launchSettings.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));

    builder.Services.AddSingleton<EndpointTemplateReader>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    var register = async (RegisterEndpointRequest request, MyceliumClient myceliumClient, EndpointTemplateReader templates) =>
        await HandleRegisterEndpointRequestAsync(request, myceliumClient, templates);

    var handleEndpoint = app.MapPost("/handle", register);
    if (authEnabled) handleEndpoint.RequireAuthorization();

    var registerEndpoint = app.MapPost("/register", register);
    if (authEnabled) registerEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Delta" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down Delta" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception error)
{
    return ServiceHost.FailedToStart("Delta", error);
}
finally
{
    Log.CloseAndFlush();
}

return 0;

static async Task<IResult> HandleRegisterEndpointRequestAsync(
    RegisterEndpointRequest request,
    MyceliumClient myceliumClient,
    EndpointTemplateReader templates)
{
    MyceliumClient.MyceliumThing? registeredThing = null;
    try
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Request must include a non-empty thing name." });

        if (request.Properties == null)
            return Results.BadRequest(new { error = "Request must include properties." });

        // Template selection is an `is` relationship, not a scalar field. No `is` row means the root.
        var isRelationships = (request.Relationships ?? new List<RequestedRelationship>())
            .Where(r => r != null
                && string.Equals(r.Predicate, "is", StringComparison.Ordinal)
                && string.Equals(r.Subject, request.Name, StringComparison.Ordinal))
            .ToList();
        if (isRelationships.Count > 1)
            return Results.BadRequest(new { error = "Registration declares more than one 'is' relationship." });

        var templateName = isRelationships.Count == 1 ? isRelationships[0].Target : null;
        if (isRelationships.Count == 1 && string.IsNullOrWhiteSpace(templateName))
            return Results.BadRequest(new { error = "Registration 'is' relationship has an empty target template." });

        // url stays request-required even though it is a structural key in the chain.
        if (!JsonValueCoercion.TryGetStringProperty(request.Properties, "url", out var url) || string.IsNullOrWhiteSpace(url))
            return Results.BadRequest(new { error = "Endpoint url must be a non-empty string." });

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return Results.BadRequest(new { error = $"Invalid endpoint url: {url}" });

        var (template, refusal) = await templates.ReadAsync(templateName);
        if (template is null)
        {
            return refusal!.StatusCode >= 500
                ? Results.Problem(detail: refusal.Reason, statusCode: refusal.StatusCode, title: "Registration failed")
                : Results.Json(new { error = refusal.Reason }, statusCode: refusal.StatusCode);
        }

        if (template.Properties.Count == 0)
        {
            return Results.Problem(
                detail: $"Endpoint template '{template.Name}' declares no properties.",
                statusCode: 500,
                title: "Registration failed");
        }

        // The broker names an inherited key by the path of archetypes it comes through ("Endpoint.url"), so a
        // key is looked up the way Tributary looks it up when it calls the endpoint.
        var invalidKeys = request.Properties.Keys
            .Where(key => !EffectivePropertyResolver.TryGetEffectiveProperty(template.Properties, key, out _, out var conflicts)
                && conflicts is null)
            .ToList();
        if (invalidKeys.Count > 0)
        {
            return Results.BadRequest(new
            {
                error = $"Endpoint thing has unsupported properties: {string.Join(", ", invalidKeys)}"
            });
        }

        // httpMethod is inheritable: validate the effective value (request over template), not the body alone.
        string? effectiveMethod = null;
        if (JsonValueCoercion.TryGetStringProperty(request.Properties, "httpMethod", out var requestMethod)
            && !string.IsNullOrWhiteSpace(requestMethod))
            effectiveMethod = requestMethod;
        else if (EffectivePropertyResolver.TryGetEffectiveProperty(template.Properties, "httpMethod", out var templateMethod, out _)
            && templateMethod.ValueKind == JsonValueKind.String)
            effectiveMethod = templateMethod.GetString();

        if (string.IsNullOrWhiteSpace(effectiveMethod))
            return Results.BadRequest(new { error = "Endpoint httpMethod must be a non-empty string." });

        var normalizedMethod = effectiveMethod.Trim().ToUpperInvariant();
        if (!HttpMethodValidator.IsSupportedMethod(normalizedMethod))
            return Results.BadRequest(new { error = $"Unsupported httpMethod: {effectiveMethod}" });

        registeredThing = await myceliumClient.CreateThingAsync(new RegisterEndpointRequest
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

        // Mycelium awaits the is-handler synchronously, so inherited properties exist once this returns.
        var relationshipCreated = await myceliumClient.CreateRelationshipAsync(
            registeredThing.Value.Id,
            template.IsPredicateId,
            template.Id);

        if (!relationshipCreated)
        {
            await CompensateAsync(myceliumClient, registeredThing.Value.Id);
            return Results.Problem(
                detail: "Failed to create 'is' relationship for registered endpoint.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Set user-supplied values only; inherited values resolve via the is-chain and are not materialized.
        foreach (var (propName, propValue) in request.Properties)
        {
            var set = await myceliumClient.SetThingPropertyAsync(registeredThing.Value.Id, propName, propValue);
            if (!set)
            {
                await CompensateAsync(myceliumClient, registeredThing.Value.Id);
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
            endpointTemplateId = template.Id,
            predicateId = template.IsPredicateId
        });
    }
    catch (Exception ex)
    {
        if (registeredThing != null)
            await CompensateAsync(myceliumClient, registeredThing.Value.Id);

        Log.Error(ex, "Error registering endpoint {Name}", request.Name);
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Failed to register endpoint");
    }
}

static async Task CompensateAsync(MyceliumClient myceliumClient, Guid thingId)
{
    Log.Warning("Compensating: deleting orphaned thing {ThingId}", thingId);
    var deleted = await myceliumClient.DeleteThingAsync(thingId);
    if (!deleted)
        Log.Error("Compensation failed: could not delete orphaned thing {ThingId}", thingId);
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
