using vos.Auth.Shared;
using vos.Service.Xylem.Configuration;
using vos.Service.Shared;
using vos.Service.Shared.Configuration;
using vos.Service.Xylem.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var launchSettings = XylemLaunchSettings.Parse(args, builder.Configuration);
if (launchSettings == null)
{
    Console.WriteLine(XylemLaunchSettings.UsageMessage);
    Environment.Exit(1);
    return;
}

var logPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs", "xylem-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "Xylem")
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true)
    .CreateLogger();

try
{
    Log.Information("VillageOS Xylem (IFC ingestion) — Port: {Port}, Mycelium: {MyceliumUrl}", launchSettings.Service.Port, launchSettings.Service.MyceliumUrl);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{launchSettings.Service.Port}");
    // Allow large IFC uploads: raise Kestrel's request-body cap and the multipart limit to the configured
    // maximum (Kestrel defaults to ~30 MB, the form reader to ~128 MB). The endpoint still enforces the cap.
    builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = launchSettings.MaxUploadBytes);
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
        o.MultipartBodyLengthLimit = launchSettings.MaxUploadBytes);
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(launchSettings.Service.VerificationKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(launchSettings.Service.VerificationKey!, issuer: launchSettings.Service.Issuer, audience: launchSettings.Service.Audience);
        Log.Information("JWT authentication enabled (issuer={Issuer}, audience={Audience})", launchSettings.Service.Issuer, launchSettings.Service.Audience);
    }

    // One credential for the whole service: the ingest tool and the model clear present the same thing,
    // and a key is exchanged once rather than once per caller.
    builder.Services.AddSingleton(sp => new ServiceCredential(
        sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<ServiceCredential>>(),
        launchSettings.Service.MyceliumUrl, launchSettings.Service.Token, launchSettings.Service.ApiKey));
    builder.Services.AddSingleton<IModelIngestRunner>(sp => new ModelIngestRunner(
        launchSettings.ModelIngestDll ?? "", launchSettings.Service.MyceliumUrl,
        sp.GetRequiredService<ServiceCredential>(), sp.GetRequiredService<ILogger<ModelIngestRunner>>()));
    builder.Services.AddSingleton<IModelPreparer>(sp => new HttpModelPreparer(
        sp.GetRequiredService<IHttpClientFactory>(), launchSettings.Service.MyceliumUrl,
        sp.GetRequiredService<ServiceCredential>()));
    builder.Services.AddSingleton(sp => new IngestHandler(
        sp.GetRequiredService<IModelIngestRunner>(), sp.GetRequiredService<IModelPreparer>()));
    builder.Services.AddSingleton<IngestJobStore>();

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Accept an uploaded .ifc (multipart: file, name, mode=merge|new-model), run ModelIngest, apply the
    // graph to the model, and return the counts. Antiforgery is disabled — this is a token-authed
    // service endpoint, not a browser form.
    var ingest = app.MapPost("/ingest", async (HttpRequest req, IngestHandler handler, IngestJobStore jobs, CancellationToken ct) =>
    {
        if (!req.HasFormContentType)
            return Results.BadRequest(new { error = "Expected a multipart/form-data upload (fields: file, name, mode)." });

        var form = await req.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null)
            return Results.BadRequest(new { error = "No IFC file uploaded (form field 'file')." });

        var name = form["name"].ToString();
        var mode = string.Equals(form["mode"].ToString(), "new-model", StringComparison.OrdinalIgnoreCase)
            ? IngestMode.NewModel
            : IngestMode.Merge;

        // Async (?async=true): spool the upload now, then ingest in the background so a large file does not
        // hold the request open — the client polls GET /ingest/jobs/{id}.
        if (string.Equals(req.Query["async"], "true", StringComparison.OrdinalIgnoreCase))
        {
            var temp = Path.Combine(Path.GetTempPath(), $"xylem_{Guid.NewGuid():N}.ifc");
            long written;
            await using (var s = file.OpenReadStream())
                written = await UploadSpooler.SpoolAsync(s, temp, launchSettings.MaxUploadBytes, ct);
            if (written <= 0)
            {
                if (File.Exists(temp)) File.Delete(temp);
                return Results.BadRequest(new { error = written == 0 ? "No IFC content uploaded." : "File exceeds the upload limit." });
            }

            var job = jobs.Create();
            _ = Task.Run(async () =>
            {
                try { jobs.Complete(job.Id, await handler.IngestAsync(name, mode, temp, CancellationToken.None)); }
                catch (Exception ex) { jobs.Complete(job.Id, IngestResult.Failed(ex.Message)); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            });
            return Results.Accepted($"/ingest/jobs/{job.Id}", new { jobId = job.Id, status = "running" });
        }

        await using var stream = file.OpenReadStream();
        var result = await handler.IngestUploadAsync(stream, name, mode, launchSettings.MaxUploadBytes, ct);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }).DisableAntiforgery();
    if (authEnabled) ingest.RequireAuthorization();

    var jobStatus = app.MapGet("/ingest/jobs/{id}", (string id, IngestJobStore jobs) =>
    {
        var job = jobs.Get(id);
        return job is null ? Results.NotFound(new { error = "Unknown ingest job." }) : Results.Ok(job);
    });
    if (authEnabled) jobStatus.RequireAuthorization();

    app.MapGet("/health", () => new { status = "Healthy", service = "Xylem" });
    app.MapGet("/stats", () => new { service = "Xylem", version = "1.0.0", myceliumUrl = launchSettings.Service.MyceliumUrl });

    var shutdown = app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Run(async () => { await Task.Delay(300); lifetime.StopApplication(); });
        return new { message = "Shutting down Xylem service" };
    });
    if (authEnabled) shutdown.RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Xylem service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
