using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Shared;
using vos.Service.Shared.Hosting;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Shared.Tests.Hosting;

public class ServiceHostTests
{
    private const string ServiceName = "Echo";
    private const string MyceliumUrl = "http://localhost:7243";

    private static WebApplication BuildApp(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        bool withMyceliumRegistration = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var handler = new MockHttpMessageHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        builder.Services.AddSingleton(new EndpointServiceMyceliumClient(
            new PerCallHttpClientFactory(handler),
            NullLogger<EndpointServiceMyceliumClient>.Instance,
            ServiceName,
            MyceliumUrl,
            "svc-token"));

        if (withMyceliumRegistration)
            builder.Services.AddMyceliumRegistration(ServiceName, port: 7100);

        return builder.Build();
    }

    [Fact]
    public async Task MapHealthAndStats_ReportsTheServiceAsHealthy()
    {
        await using var app = BuildApp();
        app.MapHealthAndStats(ServiceName, MyceliumUrl);
        await app.StartAsync();

        var body = await app.GetTestClient().GetFromJsonAsync<JsonElement>("/health");

        body.GetProperty("status").GetString().Should().Be("Healthy");
        body.GetProperty("service").GetString().Should().Be(ServiceName);
    }

    [Fact]
    public async Task MapHealthAndStats_ReportsTheHandlerIdentityAndBrokerAddress()
    {
        await using var app = BuildApp();
        app.MapHealthAndStats(ServiceName, MyceliumUrl);
        await app.StartAsync();

        var expectedHandlerId = app.Services.GetRequiredService<EndpointServiceMyceliumClient>().HandlerId;

        var body = await app.GetTestClient().GetFromJsonAsync<JsonElement>("/stats");

        body.GetProperty("service").GetString().Should().Be(ServiceName);
        body.GetProperty("handlerId").GetString().Should().Be(expectedHandlerId.ToString());
        body.GetProperty("myceliumUrl").GetString().Should().Be(MyceliumUrl);
        body.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MapShutdown_AnswersBeforeStoppingSoTheCallerSeesTheAcknowledgement()
    {
        await using var app = BuildApp();
        app.MapShutdown(ServiceName);
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/shutdown", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain(ServiceName);
    }

    [Fact]
    public async Task MapShutdown_StopsTheService()
    {
        await using var app = BuildApp();
        app.MapShutdown(ServiceName);
        await app.StartAsync();

        var stopping = new TaskCompletionSource();
        app.Lifetime.ApplicationStopping.Register(stopping.SetResult);

        await app.GetTestClient().PostAsync("/shutdown", content: null);

        var stopped = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        stopped.Should().Be(stopping.Task, "the shutdown endpoint should stop the host");
    }

    [Fact]
    public async Task AddMyceliumRegistration_AnnouncesTheServiceOnceItIsUp()
    {
        var registered = new TaskCompletionSource<HttpRequestMessage>();
        await using var app = BuildApp(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/mycelium/register")
                registered.TrySetResult(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, withMyceliumRegistration: true);

        await app.StartAsync();

        var completed = await Task.WhenAny(registered.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(registered.Task, "startup should announce the service to the broker");
        (await registered.Task).Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task AddMyceliumRegistration_WithdrawsTheServiceOnShutdown()
    {
        var withdrawn = new TaskCompletionSource<HttpRequestMessage>();
        await using var app = BuildApp(request =>
        {
            if (request.Method == HttpMethod.Delete)
                withdrawn.TrySetResult(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, withMyceliumRegistration: true);

        await app.StartAsync();
        await app.StopAsync();

        // The host awaits the withdrawal, so it has already happened by the time StopAsync returns.
        withdrawn.Task.IsCompleted.Should().BeTrue("shutdown should withdraw the service from the broker");
    }

    // A broker that is slow, absent or refusing must not stop the service coming up.
    [Fact]
    public async Task AddMyceliumRegistration_WhenTheBrokerFails_TheServiceStillServes()
    {
        await using var app = BuildApp(
            _ => throw new HttpRequestException("mycelium is unreachable"),
            withMyceliumRegistration: true);
        app.MapHealthAndStats(ServiceName, MyceliumUrl);

        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Mirrors the path ConfigureLogging builds — a shared folder four levels above the binary.
    private static DirectoryInfo LogDirectory() => new(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs"));

    private static FileInfo[] LogFilesNamed(string prefix)
    {
        var directory = LogDirectory();
        return directory.Exists ? directory.GetFiles($"{prefix}*.log") : [];
    }

    [Fact]
    public void ConfigureLogging_WithoutTheFileSink_WritesNoLogFile()
    {
        const string prefix = "service-host-no-file-test-";
        var before = LogFilesNamed(prefix).Length;

        ServiceHost.ConfigureLogging("ServiceHostTest", $"{prefix}.log", writeToFile: false);
        Serilog.Log.Information("a message that must not reach a file");
        Serilog.Log.CloseAndFlush();

        LogFilesNamed(prefix).Should().HaveCount(before);
    }

    [Fact]
    public void ConfigureLogging_WithTheFileSink_WritesTheMessageAndNamesTheService()
    {
        const string prefix = "service-host-file-test-";
        foreach (var stale in LogFilesNamed(prefix)) stale.Delete();

        try
        {
            ServiceHost.ConfigureLogging("ServiceHostFileTest", $"{prefix}.log");
            Serilog.Log.Information("a message that must reach the file");
            Serilog.Log.CloseAndFlush();

            var written = LogFilesNamed(prefix);
            written.Should().ContainSingle();
            File.ReadAllText(written[0].FullName).Should().Contain("a message that must reach the file");
        }
        finally
        {
            foreach (var file in LogFilesNamed(prefix)) file.Delete();
        }
    }
}
