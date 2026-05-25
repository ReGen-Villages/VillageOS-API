using System.Text;
using System.Text.Json;
using vos.Auth.Shared;
using vos.ManagedMicroservice.Tributary.Configuration;
using vos.ManagedMicroservice.Tributary.Helpers;
using vos.ManagedMicroservice.Tributary.Models;
using vos.ManagedMicroservice.Tributary.Services;
using vos.ManagedMicroservice.Shared.Validation;
using Jsonata.Net.Native;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var cliArgs = CliArgs.Parse(args, builder.Configuration);
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

// Skip the file sink when running under WebApplicationFactory<Program> tests. Same
// rationale as Metabolism — file I/O under the test host has no value and invites flakiness.
var isTestingEnv = builder.Environment.IsEnvironment("Testing");

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Tributary");

if (!isTestingEnv)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "tributary-.log");
    loggerConfig = loggerConfig.WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("VillageOS Tributary Service - Port: {Port}, Broker: {BrokerUrl}", servicePort, brokerUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    // Add JWT auth if broker provided a signing key. Bug #5391: use the
    // issuer/audience the broker passes via CLI so validation matches what
    // the broker signed.
    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddBrokerTokenAuth(
            signingKey!,
            issuer: cliArgs.Issuer ?? "VillageOS",
            audience: cliArgs.Audience ?? "VosClients");
        Log.Information("JWT authentication enabled for incoming broker requests (issuer={Issuer}, audience={Audience})",
            cliArgs.Issuer ?? "VillageOS", cliArgs.Audience ?? "VosClients");
    }

    builder.Services.AddSingleton(sp =>
        new BrokerClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<BrokerClient>>(),
            brokerUrl,
            serviceToken));
    builder.Services.AddSingleton<IEndpointBrokerClient>(sp => sp.GetRequiredService<BrokerClient>());
    builder.Services.AddSingleton<ObservationIngestService>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    var handleEndpoint = app.MapPost("/handle", async (
        EndpointCallRequest request,
        BrokerClient brokerClient,
        IHttpClientFactory httpClientFactory,
        ObservationIngestService observationService) =>
    {
        if (string.IsNullOrWhiteSpace(request.EndpointName))
            return Results.BadRequest(new { error = "Request must include a non-empty endpointName." });

        var thing = await brokerClient.FindThingByNameAsync(request.EndpointName);
        if (thing == null)
            return Results.NotFound(new { error = $"Endpoint thing not found: {request.EndpointName}" });

        var effective = await brokerClient.GetEffectivePropertiesAsync(thing.Value.Id);
        if (effective == null)
        {
            return Results.Problem(
                detail: "Failed to resolve effective properties for endpoint thing.",
                statusCode: 500,
                title: "Endpoint resolution failed");
        }

        var hasOverrideTransform = !string.IsNullOrWhiteSpace(request.ResponseTransform);
        JsonataQuery? overrideQuery = null;
        if (hasOverrideTransform)
        {
            try
            {
                overrideQuery = new JsonataQuery(request.ResponseTransform!);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new
                {
                    error = "Invalid responseTransform JSONata expression.",
                    detail = ex.Message
                });
            }

            var setOverride = await brokerClient.SetThingPropertyAsync(
                thing.Value.Id,
                "responseTransform",
                request.ResponseTransform);

            if (!setOverride)
            {
                return Results.Problem(
                    detail: "Failed to persist responseTransform override to endpoint thing.",
                    statusCode: 502,
                    title: "Endpoint update failed");
            }
        }

        List<string>? urlConflicts = null;
        List<string>? methodConflicts = null;
        List<string>? transformConflicts = null;

        if (!EffectivePropertyResolver.TryGetEffectiveProperty(effective, "url", out var urlElement, out urlConflicts) ||
            !EffectivePropertyResolver.TryGetEffectiveProperty(effective, "httpMethod", out var methodElement, out methodConflicts))
        {
            if (urlConflicts != null || methodConflicts != null)
            {
                return Results.BadRequest(new
                {
                    error = "Endpoint thing has ambiguous properties for url/httpMethod.",
                    conflicts = new
                    {
                        url = urlConflicts,
                        httpMethod = methodConflicts
                    }
                });
            }

            return Results.BadRequest(new
            {
                error = "Endpoint thing is missing required properties: url, httpMethod"
            });
        }

        string? responseTransform = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "responseTransform", out var transformElement, out transformConflicts))
        {
            responseTransform = transformElement.ValueKind == JsonValueKind.String
                ? transformElement.GetString()
                : transformElement.ToString();
        }

        if (!string.IsNullOrWhiteSpace(responseTransform))
            Log.Information("Endpoint {EndpointName} responseTransform: {Transform}", request.EndpointName, responseTransform);

        if (transformConflicts != null)
        {
            return Results.BadRequest(new
            {
                error = "Endpoint thing has ambiguous properties for responseTransform.",
                conflicts = new
                {
                    responseTransform = transformConflicts
                }
            });
        }

        var url = urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : urlElement.ToString();
        var method = methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : methodElement.ToString();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(method))
            return Results.BadRequest(new { error = "Endpoint url/httpMethod must be non-empty strings." });

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpointUri))
            return Results.BadRequest(new { error = $"Invalid endpoint url: {url}" });

        var normalizedMethod = method!.Trim().ToUpperInvariant();
        if (!HttpMethodValidator.IsSupportedMethod(normalizedMethod))
            return Results.BadRequest(new { error = $"Unsupported httpMethod: {method}" });

        try
        {
            var (status, body, contentType) = await CallEndpointAsync(
                httpClientFactory,
                endpointUri,
                normalizedMethod,
                request.Body);

            if (hasOverrideTransform && overrideQuery != null && status is >= 200 and < 300)
            {
                var ingestResult = await observationService.CreateObservationsAsync(thing.Value.Id, overrideQuery, body);
                if (!ingestResult.Success)
                {
                    return Results.BadRequest(new
                    {
                        error = ingestResult.Error,
                        detail = ingestResult.Detail,
                        index = ingestResult.Index,
                        observationThingId = ingestResult.ObservationThingId
                    });
                }

                return Results.Ok(new
                {
                    success = true,
                    endpointThingId = thing.Value.Id,
                    observedCount = ingestResult.ObservedCount,
                    message = "Observations created and related to endpoint."
                });
            }

            if (!string.IsNullOrWhiteSpace(responseTransform) && status is >= 200 and < 300)
            {
                JsonataQuery propertyQuery;
                try
                {
                    propertyQuery = new JsonataQuery(responseTransform!);
                }
                catch (Exception ex)
                {
                    return Results.Problem(
                        detail: ex.Message,
                        statusCode: 502,
                        title: "Endpoint response transform failed");
                }

                if (!observationService.TryTransform(body, propertyQuery, out var transformed, out var transformError))
                {
                    return Results.Problem(
                        detail: transformError,
                        statusCode: 502,
                        title: "Endpoint response transform failed");
                }

                return Results.Content(transformed, "application/json");
            }

            return Results.Content(body, contentType ?? "application/json");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Endpoint call failed for {EndpointName}", request.EndpointName);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 502,
                title: "Endpoint call failed");
        }
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Tributary" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down Tributary" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Tributary terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static bool MethodSupportsBody(string method) =>
    method is "POST" or "PUT" or "PATCH";

static async Task<(int StatusCode, string Body, string? ContentType)> CallEndpointAsync(
    IHttpClientFactory httpClientFactory,
    Uri endpointUri,
    string method,
    JsonElement body)
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(30);

    using var request = new HttpRequestMessage(new HttpMethod(method), endpointUri);
    if (MethodSupportsBody(method) && body.ValueKind != JsonValueKind.Undefined)
    {
        var json = JsonSerializer.Serialize(body);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
    }

    using var response = await client.SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    var contentType = response.Content.Headers.ContentType?.ToString();
    return ((int)response.StatusCode, content, contentType);
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/MICROSERVICE-TEMPLATE.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
