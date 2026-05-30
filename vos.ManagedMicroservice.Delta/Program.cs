using vos.Auth.Shared;
using vos.ManagedMicroservice.Delta.Configuration;
using vos.ManagedMicroservice.Delta.Helpers;
using vos.ManagedMicroservice.Delta.Models;
using vos.ManagedMicroservice.Delta.Services;
using vos.ManagedMicroservice.Shared.Validation;
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
// rationale as Metabolism/Tributary — file I/O under the test host has no value and invites
// flakiness on shared CI agents.
var isTestingEnv = builder.Environment.IsEnvironment("Testing");

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Delta");

if (!isTestingEnv)
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "delta-.log");
    loggerConfig = loggerConfig.WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("VillageOS Delta Service - Port: {Port}, Broker: {BrokerUrl}", servicePort, brokerUrl);

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

    builder.Services.AddSingleton<IEndpointSeedProvider, FileEndpointSeedProvider>();

    var app = builder.Build();
    // LoadGraph validates the whole template graph; an invalid graph throws here and fails boot.
    // The handler consumes the full graph for template selection, closed-set descent verification,
    // and effective-value resolution along the inheritance chain (Task #5467).
    var graph = app.Services.GetRequiredService<IEndpointSeedProvider>().LoadGraph();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Endpoint-service entry point used by broker /api/endpoints/{subdomain}.
    var handleEndpoint = app.MapPost("/handle", async (RegisterEndpointRequest request, BrokerClient brokerClient) =>
    {
        return await HandleRegisterEndpointRequestAsync(request, brokerClient, graph);
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    // Alias that uses the same registration logic.
    var registerEndpoint = app.MapPost("/register", async (RegisterEndpointRequest request, BrokerClient brokerClient) =>
    {
        return await HandleRegisterEndpointRequestAsync(request, brokerClient, graph);
    });
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
catch (Exception ex)
{
    Log.Fatal(ex, "Delta terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Single-active-model assumption: this handler's broker writes (find/create the default
// Endpoint thing, create the registered thing + its `is` relationship) target whichever model
// Delta's --token is scoped to. The broker launches one shared endpoint daemon and does not yet
// propagate the caller's model on /handle, so true per-model routing is deferred — see
// docs/FUTURE_ARCHITECTURE.md section 5 and Feature #5478.
static async Task<IResult> HandleRegisterEndpointRequestAsync(
    RegisterEndpointRequest request,
    BrokerClient brokerClient,
    EndpointSeedGraph graph)
{
    BrokerClient.BrokerThing? registeredThing = null;
    try
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Request must include a non-empty thing name." });

        if (request.Properties == null)
            return Results.BadRequest(new { error = "Request must include properties." });

        // Template selection is model-native: an `is` relationship from the new thing to a template,
        // not a scalar field (a vos.Thing has none). No `is` row means the root Endpoint.
        var isRelationships = (request.Relationships ?? new List<SeedRelationship>())
            .Where(r => r != null
                && string.Equals(r.Predicate, "is", StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.Subject, request.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (isRelationships.Count > 1)
            return Results.BadRequest(new { error = "Registration declares more than one 'is' relationship." });

        var templateName = isRelationships.Count == 1 ? isRelationships[0].Target : graph.Root.Name;
        if (string.IsNullOrWhiteSpace(templateName))
            return Results.BadRequest(new { error = "Registration 'is' relationship has an empty target template." });

        // Descent verification is a closed-set membership check — the graph is single-rooted and
        // acyclic, so a known template necessarily descends from the root; an unknown one does not.
        // No broker round-trip.
        if (!graph.ContainsTemplate(templateName))
        {
            return Results.BadRequest(new
            {
                error = $"Unknown endpoint template '{templateName}'; it does not descend from the root '{graph.Root.Name}'."
            });
        }

        var isPredicate = await brokerClient.FindThingByNameAsync("is");
        if (isPredicate == null)
        {
            return Results.Problem(
                detail: "Missing required 'is' predicate thing in broker model.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Admissible keys are the union of property keys along the nominated template's chain.
        var allowedSet = graph.AllowedKeys(templateName);
        if (allowedSet.Count == 0)
        {
            return Results.Problem(
                detail: "Endpoint seed does not define any allowed properties.",
                statusCode: 500,
                title: "Registration failed");
        }

        var invalidKeys = request.Properties.Keys.Where(k => !allowedSet.Contains(k)).ToList();
        if (invalidKeys.Count > 0)
        {
            return Results.BadRequest(new
            {
                error = $"Endpoint thing has unsupported properties: {string.Join(", ", invalidKeys)}"
            });
        }

        // url stays request-required even though it is a structural key in the chain.
        if (!JsonValueCoercion.TryGetStringProperty(request.Properties, "url", out var url) || string.IsNullOrWhiteSpace(url))
            return Results.BadRequest(new { error = "Endpoint url must be a non-empty string." });

        // httpMethod is inheritable: validate the effective value — the request body merged over the
        // in-memory seed chain (closest-ancestor-wins) — not the body alone.
        string? effectiveMethod = null;
        if (JsonValueCoercion.TryGetStringProperty(request.Properties, "httpMethod", out var requestMethod)
            && !string.IsNullOrWhiteSpace(requestMethod))
            effectiveMethod = requestMethod;
        else if (graph.TryGetEffectiveSeedValue(templateName, "httpMethod", out var seedMethod))
            effectiveMethod = seedMethod?.ToString();

        if (string.IsNullOrWhiteSpace(effectiveMethod))
            return Results.BadRequest(new { error = "Endpoint httpMethod must be a non-empty string." });

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return Results.BadRequest(new { error = $"Invalid endpoint url: {url}" });

        var normalizedMethod = effectiveMethod.Trim().ToUpperInvariant();
        if (!HttpMethodValidator.IsSupportedMethod(normalizedMethod))
            return Results.BadRequest(new { error = $"Unsupported httpMethod: {effectiveMethod}" });

        // Resolve the nominated template thing in the broker; create it from its seed properties if
        // absent. Boot-time creation of the whole template graph (wired parent-to-parent) lands under
        // Task #5468 — until then this find-or-create keeps the leaf template available on demand.
        var templateSeed = graph.Templates[templateName];
        var templateThing = await brokerClient.FindThingByNameAsync(templateSeed.Name);
        if (templateThing == null)
        {
            templateThing = await brokerClient.CreateThingAsync(new RegisterEndpointRequest
            {
                Name = templateSeed.Name,
                Properties = templateSeed.Properties ?? new Dictionary<string, object>()
            });
        }

        if (templateThing == null)
        {
            return Results.Problem(
                detail: $"Could not resolve endpoint template '{templateSeed.Name}' in broker.",
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

        // Create 'is' relationship to the nominated template. The broker awaits the is-handler
        // synchronously, so inherited properties are available before this call returns.
        var relationshipCreated = await brokerClient.CreateRelationshipAsync(
            registeredThing.Value.Id,
            isPredicate.Value.Id,
            templateThing.Value.Id);

        if (!relationshipCreated)
        {
            await CompensateAsync(brokerClient, registeredThing.Value.Id);
            return Results.Problem(
                detail: "Failed to create 'is' relationship for registered endpoint.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Set user-supplied values only; inherited values resolve via the is-chain and are not materialized.
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
            endpointTemplateId = templateThing.Value.Id,
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

// Exposed to WebApplicationFactory<Program> in the test project per docs/MICROSERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
