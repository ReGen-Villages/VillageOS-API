using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using vos.Service.Feedback;
using vos.Service.Feedback.Configuration;
using vos.Service.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

var (settings, whyRefused) = FeedbackLaunchSettings.Parse(args, builder.Configuration);
if (settings is null)
{
    Console.WriteLine(whyRefused);
    Environment.Exit(1);
    return;
}

ServiceHost.ConfigureLogging("Feedback", "feedback-.log", writeToFile: !builder.Environment.IsEnvironment("Testing"));

try
{
    Log.Information("VillageOS Feedback relay — Port: {Port}, platform: {Platform}, filing in {Organisation}",
        settings.Service.Port, settings.Service.MyceliumUrl, settings.DevOpsOrganization);

    builder.Host.UseSerilog();
    builder.WebHost.UseUrls($"http://localhost:{settings.Service.Port}");
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = ReportLimits.MaximumBodyBytes);
    builder.Services.AddHttpClient();

    // Callers arrive through the reverse proxy, so the connection is from loopback and the caller's own
    // address is in the forwarded header. Without this every reporter would share one budget.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.AddPolicy(ReportRate.PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = ReportRate.RequestsAllowed, Window = ReportRate.Window }));

        options.OnRejected = async (context, cancellation) =>
        {
            var comeBackIn = (int)Math.Ceiling(
                (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : ReportRate.Window).TotalSeconds);
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.RetryAfter = comeBackIn.ToString();
            await context.HttpContext.Response.WriteAsJsonAsync(
                new Refusal(RefusalCode.TooManyRequests, $"Too many reports. Try again in {comeBackIn} seconds.",
                    new Dictionary<string, object?> { ["seconds"] = comeBackIn }),
                cancellation);
        };
    });

    if (settings.AllowedOrigins.Length > 0)
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(settings.AllowedOrigins)
            .WithMethods("POST")
            .WithHeaders("Content-Type", "Authorization")));

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(settings.Destinations);
    builder.Services.AddSingleton(provider => new PlatformCallers(
        provider.GetRequiredService<IHttpClientFactory>(), settings.Service.MyceliumUrl));
    builder.Services.AddSingleton(provider => new DevOpsWorkItems(
        provider.GetRequiredService<IHttpClientFactory>(), settings.DevOpsOrganization, settings.DevOpsAccessToken));
    builder.Services.AddSingleton<ReportFiling>();

    var app = builder.Build();

    app.UseForwardedHeaders();
    if (settings.AllowedOrigins.Length > 0)
        app.UseCors();
    app.UseRateLimiter();

    app.MapHealth("Feedback");

    app.MapPost("/reports", async (HttpContext context, ReportFiling filing) =>
    {
        if (context.Request.ContentLength > ReportLimits.MaximumBodyBytes)
            return ReportFiling.Answer(StatusCodes.Status413PayloadTooLarge,
                new Refusal(RefusalCode.ReportTooLarge, "The report is too large. Send a smaller screenshot."));

        var authorization = context.Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization["Bearer ".Length..].Trim() : null;
        return await filing.FileAsync(token, async cancellation =>
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<ReportRequest>(context.Request.Body, JsonSerializerOptions.Web, cancellation);
            }
            catch (JsonException)
            {
                return null;
            }
        }, context.RequestAborted);
    }).RequireRateLimiting(ReportRate.PolicyName);

    app.Run();
}
catch (Exception error) when (error is not HostAbortedException)
{
    Log.Fatal(error, "Feedback relay terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
