using vos.Auth.Shared;
using vos.Service.Delta.Helpers;
using vos.Service.Delta.Models;
using vos.Service.Delta.Services;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Configuration;
using vos.Service.Shared.Validation;
using Serilog;


var builder = WebApplication.CreateBuilder(args);

var launchSettings = ServiceLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ServiceLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = launchSettings.Port;
var myceliumUrl = launchSettings.MyceliumUrl;
var serviceToken = launchSettings.Token;
var signingKey = launchSettings.SigningKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Delta", "delta-.log", writeToFile: !isTestingEnv);

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
            serviceToken));

    builder.Services.AddSingleton<IEndpointSeedProvider, FileEndpointSeedProvider>();

    var app = builder.Build();
    // An invalid template graph throws here and fails boot.
    var graph = app.Services.GetRequiredService<IEndpointSeedProvider>().LoadGraph();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    // Find-or-create every template thing on a model's first registration, under the token that named
    // that model. Provisioning at startup instead would put the whole catalog in the one model Delta's
    // launch token names, and one Delta process answers every project.
    var provisioner = new TemplateCatalogProvisioner(
        app.Services.GetRequiredService<MyceliumClient>(),
        graph,
        app.Services.GetRequiredService<ILogger<TemplateCatalogProvisioner>>());
    var templateCatalog = new ModelTemplateCatalog();

    var register = async (RegisterEndpointRequest request, MyceliumClient myceliumClient) =>
        await HandleRegisterEndpointRequestAsync(request, myceliumClient, graph, provisioner, templateCatalog);

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
catch (Exception ex)
{
    Log.Fatal(ex, "Delta terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static async Task<IResult> HandleRegisterEndpointRequestAsync(
    RegisterEndpointRequest request,
    MyceliumClient myceliumClient,
    EndpointSeedGraph graph,
    TemplateCatalogProvisioner provisioner,
    ModelTemplateCatalog templateCatalog)
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

        // The registration lands in the model the caller's bearer names, so the template it is wired to
        // has to be in that same model — provisioned there on the first registration it sends. Resolved
        // after the validation above so a request that was going to be refused provisions nothing.
        var callerModelId = ModelScopedBearer.Read(await myceliumClient.GetTokenAsync())?.ModelId
            ?? ModelTemplateCatalog.UnnamedModel;
        var catalog = await templateCatalog.ProvisionedForAsync(callerModelId, provisioner.ProvisionAsync);
        if (catalog == null)
        {
            return Results.Problem(
                detail: "Missing required 'is' predicate thing in mycelium model.",
                statusCode: 500,
                title: "Registration failed");
        }

        // Absent means this model's provisioning pass could not create it; a later pass would create a
        // second Thing of the same name rather than repair the first, so it is not retried here.
        var templateSeed = graph.Templates[templateName];
        if (!catalog.TemplateIdsByName.TryGetValue(templateSeed.Name, out var templateThingId))
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
            catalog.IsPredicateId,
            templateThingId);

        if (!relationshipCreated)
        {
            // The held ids are the likeliest reason the wire failed — a Thing deleted since it was
            // provisioned is still named here. Discard them so the next registration provisions again
            // rather than failing on the same two ids forever.
            templateCatalog.Forget(callerModelId);
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
            endpointTemplateId = templateThingId,
            predicateId = catalog.IsPredicateId
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
