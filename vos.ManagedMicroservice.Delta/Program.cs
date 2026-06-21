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

// Skip the file sink under tests: file I/O on shared CI agents invites flakiness.
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

    // Issuer/audience must match what Mycelium signed, hence taken from CLI (Bug #5391).
    var authEnabled = !string.IsNullOrEmpty(signingKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(
            signingKey!,
            issuer: cliArgs.Issuer,
            audience: cliArgs.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            cliArgs.Issuer, cliArgs.Audience);
    }

    builder.Services.AddSingleton(sp =>
        new MyceliumClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumClient>>(),
            myceliumUrl,
            serviceToken));

    builder.Services.AddSingleton<IEndpointSeedProvider, FileEndpointSeedProvider>();

    var app = builder.Build();
    // An invalid template graph throws here and fails boot.
    var graph = app.Services.GetRequiredService<IEndpointSeedProvider>().LoadGraph();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Find-or-create every template thing at startup so registrations never create templates lazily.
    // Skipped under tests so they make no mycelium calls at boot.
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

    var handleEndpoint = app.MapPost("/handle", async (RegisterEndpointRequest request, MyceliumClient myceliumClient) =>
    {
        return await HandleRegisterEndpointRequestAsync(request, myceliumClient, graph);
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

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

// Single-active-model assumption: writes target whichever model Delta's --token is scoped to.
// /handle does not yet propagate the caller's model, so per-model routing is deferred (Feature #5478).
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

        // Template selection is an `is` relationship, not a scalar field. No `is` row means the root.
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

        // Single-rooted + acyclic, so membership in the graph proves descent from the root.
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

        // httpMethod is inheritable: validate the effective value (request over seed chain), not the body alone.
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

        // Templates are provisioned at boot; a missing one is a provisioning failure, not repaired lazily here.
        var templateSeed = graph.Templates[templateName];
        var templateThing = await myceliumClient.FindThingByNameAsync(templateSeed.Name);
        if (templateThing == null)
        {
            return Results.Problem(
                detail: $"Endpoint template '{templateSeed.Name}' is not provisioned in mycelium.",
                statusCode: 500,
                title: "Registration failed");
        }

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
