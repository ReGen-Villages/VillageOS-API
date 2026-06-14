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
var myceliumUrl = cliArgs.MyceliumUrl;
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
    Log.Information("VillageOS Delta Service - Port: {Port}, Mycelium: {MyceliumUrl}", servicePort, myceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    // Add JWT auth if mycelium provided a signing key. Bug #5391: use the
    // issuer/audience Mycelium passes via CLI so validation matches what
    // Mycelium signed.
    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(
            signingKey!,
            issuer: cliArgs.Issuer ?? "VillageOS",
            audience: cliArgs.Audience ?? "VosClients");
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            cliArgs.Issuer ?? "VillageOS", cliArgs.Audience ?? "VosClients");
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl,
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

    // Provision the endpoint-template catalog into Mycelium once at startup (Task #5468): every
    // template thing is find-or-created and wired to its parent via `is`, so registrations never
    // create templates lazily. Gated on the built-app environment — skipped under
    // WebApplicationFactory<Program> tests, mirroring how Metabolism gates its SignalR/lifecycle
    // work — so tests make no mycelium calls at boot.
    if (!app.Environment.IsEnvironment("Testing"))
    {
        var provisioner = new TemplateCatalogProvisioner(
            app.Services.GetRequiredService<MyceliumClient>(),
            graph,
            app.Services.GetRequiredService<ILogger<TemplateCatalogProvisioner>>());

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            try
            {
                await provisioner.ProvisionAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error provisioning Delta endpoint-template catalog at startup");
            }
        }));
    }

    // Endpoint-service entry point used by mycelium /api/endpoints/{subdomain}.
    var handleEndpoint = app.MapPost("/handle", async (RegisterEndpointRequest request, MyceliumClient myceliumClient) =>
    {
        return await HandleRegisterEndpointRequestAsync(request, myceliumClient, graph);
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    // Alias that uses the same registration logic.
    var registerEndpoint = app.MapPost("/register", async (RegisterEndpointRequest request, MyceliumClient myceliumClient) =>
    {
        return await HandleRegisterEndpointRequestAsync(request, myceliumClient, graph);
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

// Single-active-model assumption: this handler's mycelium writes (resolving the boot-provisioned
// template thing, creating the registered thing + its `is` relationship) target whichever model
// Delta's --token is scoped to. The mycelium launches one shared endpoint daemon and does not yet
// propagate the caller's model on /handle, so true per-model routing is deferred — see
// docs/FUTURE_ARCHITECTURE.md section 5 and Feature #5478.
static async Task<IResult> HandleRegisterEndpointRequestAsync(
    RegisterEndpointRequest request,
    MyceliumClient myceliumClient,
    EndpointSeedGraph graph)
{
    MyceliumClient.MyceliumThing? registeredThing = null;
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
        // No mycelium round-trip.
        if (!graph.ContainsTemplate(templateName))
        {
            return Results.BadRequest(new
            {
                error = $"Unknown endpoint template '{templateName}'; it does not descend from the root '{graph.Root.Name}'."
            });
        }

        var isPredicate = await myceliumClient.FindThingByNameAsync("is");
        if (isPredicate == null)
        {
            return Results.Problem(
                detail: "Missing required 'is' predicate thing in mycelium model.",
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

        // The template catalog is provisioned at boot (TemplateCatalogProvisioner, Task #5468), so the
        // nominated template thing is expected to already exist wired to its parent. Resolve it; its
        // absence is a provisioning failure, not something the handler repairs by creating it lazily.
        var templateSeed = graph.Templates[templateName];
        var templateThing = await myceliumClient.FindThingByNameAsync(templateSeed.Name);
        if (templateThing == null)
        {
            return Results.Problem(
                detail: $"Endpoint template '{templateSeed.Name}' is not provisioned in mycelium.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Create thing with name only - no own properties.
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

        // Create 'is' relationship to the nominated template. The mycelium awaits the is-handler
        // synchronously, so inherited properties are available before this call returns.
        var relationshipCreated = await myceliumClient.CreateRelationshipAsync(
            registeredThing.Value.Id,
            isPredicate.Value.Id,
            templateThing.Value.Id);

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
            endpointTemplateId = templateThing.Value.Id,
            predicateId = isPredicate.Value.Id
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

// Exposed to WebApplicationFactory<Program> in the test project per docs/MICROSERVICES.md.
// Top-level statements compile to a `Program` class that is internal by default — this empty
// partial declaration just elevates it to public so the test factory can name it.
public partial class Program { }
