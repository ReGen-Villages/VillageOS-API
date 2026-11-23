using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
#if VOS_ENGINE_IS_STAGED
using Microsoft.AspNetCore.Hosting;
using vos.Mycelium.Testing;
#endif
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.BrokerContract.Tests.AgainstTheEngine;

// The engine keeps its subscriptions in memory. Only the engine can say what it answers for one it no longer
// holds, which is what the client reads as a subscription that is gone.
public sealed class AServiceSubscribesAgainAfterTheEngineRestartsTests : IClassFixture<AnEngineThatRestarts>
{
    private readonly AnEngineThatRestarts _engine;

    public AServiceSubscribesAgainAfterTheEngineRestartsTests(AnEngineThatRestarts engine) => _engine = engine;

    private SubscriptionClient ClientWith(Func<Task<string?>> token) => new(
        _engine.ClientFactory, NullLogger<SubscriptionClient>.Instance, TheEngine.Url, tokenProvider: token)
    {
        ReconnectDelay = TimeSpan.FromMilliseconds(50),
    };

    [FactAgainstTheEngine]
    public async Task A_subscription_deleted_at_the_engine_ends_its_stream()
    {
        var client = ClientWith(() => Task.FromResult<string?>(_engine.AdminToken));
        var opened = await client.SubscribeAsync(new SubscriptionSelector { Ids = [AnEngineThatRestarts.Study], IncludeSnapshot = false });
        using var patience = new CancellationTokenSource(Settle.Ceiling);
        var read = Task.Run(async () =>
        {
            await foreach (var change in client.StreamAsync(opened.SubscriptionId, opened.Watermark, patience.Token)) { }
        });

        await client.UnsubscribeAsync(opened.SubscriptionId);

        await read;
        patience.IsCancellationRequested.Should().BeFalse("the stream ends by itself");
    }

    [FactAgainstTheEngine]
    public async Task A_service_follows_its_subject_again_once_the_engine_is_back()
    {
        var recomputed = Channel.CreateUnbounded<Guid>();
        var inputs = new RecomputeInputs("ContractCompute",
            () => new HashSet<string>(StringComparer.Ordinal) { "population" },
            (subjectId, _) => { recomputed.Writer.TryWrite(subjectId); return Task.CompletedTask; });
        var log = new AnnouncingLogger<InputChangeRecomputeService>();
        var service = new InputChangeRecomputeService(
            ClientWith,
            new ServiceTokenExchange(_engine.ClientFactory, NullLogger<ServiceTokenExchange>.Instance, TheEngine.Url),
            inputs, new DevelopmentEnvironment(), log, launchToken: new LaunchToken(_engine.AdminToken));
        service.Watch(AnEngineThatRestarts.Study);
        await service.StartAsync(CancellationToken.None);
        await log.UntilLoggedAsync("is following its inputs");

        async Task RecomputedAsync(string why)
        {
            var next = recomputed.Reader.ReadAsync().AsTask();
            await Settle.ForAsync(next, why);
            (await next).Should().Be(AnEngineThatRestarts.Study);
        }

        try
        {
            await _engine.RestartAsync();
            await RecomputedAsync("the study is recomputed once its new stream opens");

            var edited = await _engine.Admin.PutAsJsonAsync($"/api/things/{AnEngineThatRestarts.Study}/properties",
                new { name = "population", type = "vos.Double", value = 120.0 });
            edited.EnsureSuccessStatusCode();
            await RecomputedAsync("an edit after the restart reaches the service on its new subscription");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}

// An engine that can be stopped and started again on the same seed and the same accounts, so a token it issued
// before the restart still signs a call after it, as on a server.
public sealed class AnEngineThatRestarts : IAsyncLifetime
{
    public static readonly Guid Study = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    public string AdminToken { get; private set; } = "";

    public IHttpClientFactory ClientFactory { get; private set; } = null!;

    public HttpClient Admin { get; private set; } = null!;

#if !VOS_ENGINE_IS_STAGED
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    public Task RestartAsync() => throw new InvalidOperationException(
        "No engine is staged, so this case cannot run. Mark it [FactAgainstTheEngine], which skips it here.");
#else
    private const string Seed = """
        {
          "Id": "a1000000-0000-0000-0000-000000000001",
          "Name": "Restarted engine",
          "Things": [
            { "Id": "a1000000-0000-0000-0000-000000000002", "Name": "is" },
            { "Id": "a1000000-0000-0000-0000-000000000003", "Name": "Study",
              "Properties": { "population": { "typeInfo": "vos.Double", "value": 100.0 } } }
          ],
          "Relationships": []
        }
        """;

    private readonly string _kept = Path.Combine(Path.GetTempPath(), $"restarted-engine-{Guid.NewGuid():N}");
    private readonly string _username = $"restart-admin-{Guid.NewGuid():N}";
    private readonly string _password = $"restart-password-{Guid.NewGuid():N}";
    private readonly PresentEngine _present = new();
    private EngineOnKeptAccounts? _host;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_kept);
        await File.WriteAllTextAsync(Path.Combine(_kept, "Restarted.seed.json"), Seed);
        ClientFactory = new PerCallHttpClientFactory(_present);
        Admin = new HttpClient(_present, disposeHandler: false) { BaseAddress = new Uri(TheEngine.Url) };
        await StartAsync();

        var signedIn = await Admin.PostAsJsonAsync("/api/auth/login", new { Username = _username, Password = _password });
        signedIn.EnsureSuccessStatusCode();
        AdminToken = (await signedIn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        Admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
    }

    public async Task RestartAsync()
    {
        _present.Transport = null;
        _host!.Dispose();
        await StartAsync();
    }

    private async Task StartAsync()
    {
        _host = new EngineOnKeptAccounts(Path.Combine(_kept, "auth"))
        {
            SeedFile = Path.Combine(_kept, "Restarted.seed.json"),
            AdminUsername = _username,
            AdminPassword = _password,
        };
        await _host.InitializeAsync();
        _present.Transport = new HttpMessageInvoker(_host.Server.CreateHandler());
    }

    public Task DisposeAsync()
    {
        Admin.Dispose();
        _host?.Dispose();
        Directory.Delete(_kept, recursive: true);
        return Task.CompletedTask;
    }

    private sealed class EngineOnKeptAccounts(string accounts) : MyceliumTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Auth:DataDirectory", accounts);
        }
    }

    private sealed class PresentEngine : HttpMessageHandler
    {
        private volatile HttpMessageInvoker? _transport;

        public HttpMessageInvoker? Transport { set => _transport = value; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _transport is { } transport
                ? transport.SendAsync(request, cancellationToken)
                : throw new HttpRequestException("the engine is restarting");
    }
#endif
}
