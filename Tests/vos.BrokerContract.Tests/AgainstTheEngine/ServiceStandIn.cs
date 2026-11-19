using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// A service the platform calls over HTTP, already running on a port of its own, that records each call and accepts it.
internal sealed class ServiceStandIn : IAsyncDisposable
{
    private readonly List<JsonElement> _calls = new();
    private WebApplication? _app;

    public int Port { get; } = FreeLoopbackPort();

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.Urls.Add($"http://127.0.0.1:{Port}");
        _app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
        _app.MapPost("/handle", async (HttpRequest request) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
            lock (_calls) _calls.Add(body);
            return Results.Ok(new { success = true, accepted = true });
        });
        await _app.StartAsync();
    }

    public bool CalledAbout(Guid subjectId)
    {
        lock (_calls)
            return _calls.Any(call => call.TryGetProperty("subjectId", out var subject) && subject.GetString() == subjectId.ToString());
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
