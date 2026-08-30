using vos.Auth.Shared;
using vos.Service.Forage.Configuration;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using vos.Service.Shared;
using vos.Service.Shared.DagNode;
using vos.Service.Shared.Hosting;
using vos.Service.Shared.Subscriptions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = ForageLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(ForageLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return;
}

var servicePort = launchSettings.Service.Port;
var myceliumUrl = launchSettings.Service.MyceliumUrl;
var serviceToken = launchSettings.Service.Token;
var apiKey = launchSettings.Service.ApiKey;
var verificationKey = launchSettings.Service.VerificationKey;

var isTestingEnv = builder.Environment.IsEnvironment("Testing");
ServiceHost.ConfigureLogging("Forage", "forage-.log", writeToFile: !isTestingEnv);

try
{
    Log.Information(
        "VillageOS Forage Service - Port: {Port}, Mycelium: {MyceliumUrl}, fetching through {Subdomain}",
        servicePort, myceliumUrl, launchSettings.FetcherSubdomain);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{servicePort}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(verificationKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(
            verificationKey!,
            issuer: launchSettings.Service.Issuer,
            audience: launchSettings.Service.Audience);
        Log.Information("JWT authentication enabled for incoming mycelium requests (issuer={Issuer}, audience={Audience})",
            launchSettings.Service.Issuer, launchSettings.Service.Audience);
    }

    // Coverage is edges, so the only read this service makes is a scoped snapshot.
    builder.Services.AddSingleton<ISubscriptionClient>(sp =>
        new SubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<SubscriptionClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));
    builder.Services.AddSingleton<CoveringSourceService>();
    builder.Services.AddSingleton(sp =>
        new EndpointServiceSourceFetcher(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<EndpointServiceSourceFetcher>>(),
            myceliumUrl,
            serviceToken,
            launchSettings.FetcherSubdomain,
            launchSettings.SourceTimeout,
            apiKey));
    builder.Services.AddSingleton<ISourceFetcher>(sp => sp.GetRequiredService<EndpointServiceSourceFetcher>());
    builder.Services.AddSingleton<IEndpointBodyReader>(sp => sp.GetRequiredService<EndpointServiceSourceFetcher>());
    builder.Services.AddSingleton(sp =>
        new DiscoveryRunner(
            sp.GetRequiredService<ISourceFetcher>(),
            launchSettings.MaxConcurrentSources,
            sp.GetRequiredService<ILogger<DiscoveryRunner>>()));
    builder.Services.AddSingleton(sp =>
        new MyceliumRelationshipClient(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<MyceliumRelationshipClient>>(),
            myceliumUrl,
            serviceToken, apiKey: apiKey));
    builder.Services.AddSingleton<ICoverageWriter>(sp => sp.GetRequiredService<MyceliumRelationshipClient>());
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<CoverageLedger>();
    builder.Services.AddSingleton<DivisionResolver>();
    builder.Services.AddSingleton<AnalysisSpawner>();
    builder.Services.AddSingleton<VocabularyEdgeWriter>();
    builder.Services.AddSingleton<IDiscoveryRunStarter, DiscoveryRunStarter>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMyceliumModelToken();
    }

    // A run is started by the site entering the state the discovery connection watches, so the body is
    // whatever the broker posts for a dispatch and the subject is read through the shared classifier
    // rather than a shape of this service's own.
    var handleEndpoint = app.MapPost("/handle", async (
        CoveringSourceService coveringSources,
        DiscoveryRunner runner,
        CoverageLedger ledger,
        DivisionResolver divisions,
        AnalysisSpawner analysis,
        VocabularyEdgeWriter vocabularyEdges,
        IDiscoveryRunStarter starter,
        HttpContext httpContext) =>
    {
        using var reader = new StreamReader(httpContext.Request.Body);
        var request = HandleRequestRouter.Classify(await reader.ReadToEndAsync());

        if (request.Kind != HandleRequestKind.RelationshipSubject)
            return Results.BadRequest(new { error = HandleRequestRouter.DescribeExpectedShapes("Forage") });

        // Named here so the run captures the subject alone; capturing the request would hold the parsed
        // body for as long as the run takes.
        var subjectId = request.SubjectId;
        starter.Start(token =>
            Discover(subjectId, coveringSources, runner, ledger, divisions, analysis, vocabularyEdges, token));

        // Accepted, not done — see IDiscoveryRunStarter for what closes it. `success` is the broker's
        // own contract: a body declaring it false is a failed dispatch whatever the status said.
        return Results.Accepted(value: new { success = true, subjectId });
    });
    if (authEnabled) handleEndpoint.RequireAuthorization();

    app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "Forage" }));

    var shutdownEndpoint = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            lifetime.StopApplication();
        });

        return Results.Ok(new { message = "Shutting down Forage" });
    });
    if (authEnabled) shutdownEndpoint.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Forage terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// One run. Nothing returns from here, so what it found is reported to the log. What the dispatch named
// decides which run it is: a site is fetched and recorded; a source is offered to the sites it covers.
static async Task Discover(
    Guid subjectId,
    CoveringSourceService coveringSources,
    DiscoveryRunner runner,
    CoverageLedger ledger,
    DivisionResolver divisions,
    AnalysisSpawner analysis,
    VocabularyEdgeWriter vocabularyEdges,
    CancellationToken cancellationToken)
{
    switch (await coveringSources.KindOfAsync(subjectId, cancellationToken))
    {
        case null:
            Log.Error("Could not read what {SubjectId} is; leaving the run outstanding", subjectId);
            return;
        case SubjectKind.Site:
            await DiscoverSite(subjectId, coveringSources, runner, ledger, divisions, analysis, vocabularyEdges, cancellationToken);
            return;
        case SubjectKind.ModelMarksNoSite:
            Log.Warning("The model marks no site archetype, so {SubjectId} is taken for a site; such a model " +
                        "cannot dispatch a source", subjectId);
            await DiscoverSite(subjectId, coveringSources, runner, ledger, divisions, analysis, vocabularyEdges, cancellationToken);
            return;
        case SubjectKind.Source:
            await OfferSource(subjectId, coveringSources, ledger, cancellationToken);
            return;
        default:
            Log.Error("{SubjectId} is neither a site nor a source, so the dispatch names a Thing this service " +
                      "has no run for; nothing is written", subjectId);
            return;
    }
}

// A source offered to every site under the Places it covers: a coverage minted per call those sites
// would make, and nothing fetched. A minted coverage is outstanding, which is what puts each site back in
// the state its own run is dispatched by — and that run asks only this source. The stamp is what tells a
// source offered to every site it covers from one never offered.
static async Task OfferSource(
    Guid sourceId,
    CoveringSourceService coveringSources,
    CoverageLedger ledger,
    CancellationToken cancellationToken)
{
    var coverage = await coveringSources.ForSourceAsync(sourceId, cancellationToken);
    if (coverage == null)
    {
        // Stamped, the source would read as offered to every site it covers while having reached none,
        // and nothing would offer it again. Written nothing, it stays in the state that dispatched this.
        Log.Error("Could not read which sites source {SourceId} reaches; leaving the run outstanding rather " +
                  "than recording the source as offered", sourceId);
        return;
    }

    if (coverage.Vocabulary == null)
    {
        Log.Error("The model declares no coverage archetype, so source {SourceId} cannot be offered to the " +
                  "sites it covers; nothing is written", sourceId);
        return;
    }

    if (coverage.Covering.Count == 0)
        Log.Information("Source {SourceId} has no registration to be called through; offered to no site", sourceId);

    var outstanding = await ledger.OutstandingAsync(coverage, cancellationToken);
    Log.Information("Source {SourceId} offered to the sites it reaches: {Outstanding} calls outstanding",
        sourceId, outstanding.Count);

    await ledger.StampWorkedOutAsync(sourceId, cancellationToken);
}

static async Task DiscoverSite(
    Guid siteId,
    CoveringSourceService coveringSources,
    DiscoveryRunner runner,
    CoverageLedger ledger,
    DivisionResolver divisions,
    AnalysisSpawner analysis,
    VocabularyEdgeWriter vocabularyEdges,
    CancellationToken cancellationToken)
{
    var coverage = await coveringSources.ForSiteAsync(siteId, cancellationToken);
    if (coverage == null)
    {
        // Nothing is written and the analysis is not started, so the site stays in the state that
        // dispatched this and the run is driven again. Carrying on would report that no source covers
        // the site, which is the answer an unreachable gateway must never be mistaken for.
        Log.Error("Could not read which sources cover site {SiteId}; leaving the run outstanding rather " +
                  "than reporting that none do", siteId);
        return;
    }

    // Before the fetches, because the hazard portal's address is a division code and no route it serves
    // takes coordinates: a site graded on the run after the one that resolved its division would wait out
    // a whole dispatch first.
    coverage = await divisions.AddressAsync(siteId, coverage, cancellationToken);

    // Not a failure: the source said what it resolves onto, and this site holds nothing of it.
    foreach (var source in coverage.Covering.Where(source => source.Calls.Count == 0))
        Log.Information("Source {Source} resolves onto Things site {SiteId} holds none of; nothing to fetch",
            source.Name, siteId);

    if (coverage.Vocabulary == null)
        Log.Error("The model declares no coverage archetype, so this run records nothing about what it " +
                  "found and every call it makes will be made again");

    // Only the calls whose answer is not already in. A source that answered for this subject is not
    // called a second time, and one that failed is, which is the whole of what the coverage Things buy.
    var outstanding = await ledger.OutstandingAsync(coverage, cancellationToken);
    var report = await runner.RunAsync(
        siteId, CoverageLedger.CallsStillToMake(coverage.Covering, outstanding), cancellationToken);
    await ledger.RecordAllAsync(outstanding, report, cancellationToken);

    foreach (var outcome in report.Unresolved)
        Log.Warning("Source {Source} left {Subject} undiscovered: {Reason}",
            outcome.Source, outcome.Subject ?? $"site {siteId}", outcome.Reason);

    // A fetched word becomes the edge the model declares before the analysis starts, so what the
    // analysis reads is already resolved (#6809).
    await vocabularyEdges.ResolveAsync(siteId, report, cancellationToken);

    await ledger.StampWorkedOutAsync(siteId, cancellationToken);

    // Whatever mixture resolved, including none. The analysis reports against what discovery left it, and
    // a site whose sources were all unavailable is exactly the case a planner needs the analysis to say
    // something about rather than silently never running.
    var spawn = await analysis.SpawnAsync(siteId, coverage.Analysis, cancellationToken);
    if (!spawn.Started)
        Log.Information("No analysis started for site {SiteId}: {Reason}", siteId, spawn.Reason);
}

// Exposed to WebApplicationFactory<Program> in the test project per docs/SERVICES.md.
public partial class Program { }
