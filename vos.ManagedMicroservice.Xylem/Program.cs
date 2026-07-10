using vos.Auth.Shared;
using vos.ManagedMicroservice.Xylem.Configuration;
using vos.ManagedMicroservice.Xylem.Services;
using Serilog;

var cliArgs = CliArgs.Parse(args);
if (cliArgs == null)
{
    Console.WriteLine(CliArgs.UsageMessage);
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
    Log.Information("VillageOS Xylem (IFC ingestion) — Port: {Port}, Mycelium: {MyceliumUrl}", cliArgs.Port, cliArgs.MyceliumUrl);

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{cliArgs.Port}");
    builder.Services.AddHttpClient();

    var authEnabled = !string.IsNullOrEmpty(cliArgs.SigningKey);
    if (authEnabled)
    {
        builder.AddMyceliumTokenAuth(cliArgs.SigningKey!, issuer: cliArgs.Issuer, audience: cliArgs.Audience);
        Log.Information("JWT authentication enabled (issuer={Issuer}, audience={Audience})", cliArgs.Issuer, cliArgs.Audience);
    }

    builder.Services.AddSingleton<IIfcIngestRunner>(sp => new IfcIngestRunner(
        cliArgs.IfcIngestDll ?? "", cliArgs.MyceliumUrl, cliArgs.Token, sp.GetRequiredService<ILogger<IfcIngestRunner>>()));
    builder.Services.AddSingleton<IModelPreparer>(sp => new HttpModelPreparer(
        sp.GetRequiredService<IHttpClientFactory>(), cliArgs.MyceliumUrl, cliArgs.Token));
    builder.Services.AddSingleton(sp => new IngestHandler(
        sp.GetRequiredService<IIfcIngestRunner>(), sp.GetRequiredService<IModelPreparer>()));

    var app = builder.Build();

    if (authEnabled)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }

    // Accept an uploaded .ifc (multipart: file, name, mode=merge|new-model), run IfcIngest, apply the
    // graph to the model, and return the counts. Antiforgery is disabled — this is a token-authed
    // service endpoint, not a browser form.
    var ingest = app.MapPost("/ingest", async (HttpRequest req, IngestHandler handler, CancellationToken ct) =>
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

        await using var stream = file.OpenReadStream();
        var result = await handler.IngestUploadAsync(stream, file.Length, name, mode, cliArgs.MaxUploadBytes, ct);
        return result.Success ? Results.Ok(result) : Results.BadRequest(result);
    }).DisableAntiforgery();
    if (authEnabled) ingest.RequireAuthorization();

    app.MapGet("/health", () => new { status = "Healthy", service = "Xylem" });
    app.MapGet("/stats", () => new { service = "Xylem", version = "1.0.0", myceliumUrl = cliArgs.MyceliumUrl });

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
