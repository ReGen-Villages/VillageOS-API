using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using vos.Service.Xylem.Services;
using vos.Tests.Shared;

namespace vos.Service.Xylem.Tests;

// Stands the ingest service up with the ingest tool and the model clear both answered here, so a test
// drives the routes and reads what each collaborator was asked. Settings go in through the host builder;
// the launch-setting reader falls back to them when no command-line flag is present.
public sealed class XylemWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string MyceliumUrl = "http://platform.test";

    public FakeRunner Runner { get; } = new();
    public FakePreparer Preparer { get; } = new();

    public string? VerificationKey { get; set; }
    public string? Issuer { get; set; }
    public string? Audience { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", MyceliumUrl);
        builder.UseSetting("Token", "test-token");
        if (VerificationKey != null) builder.UseSetting("VerificationKey", VerificationKey);
        if (Issuer != null) builder.UseSetting("Issuer", Issuer);
        if (Audience != null) builder.UseSetting("Audience", Audience);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(
                new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
            services.RemoveAll<IModelIngestRunner>();
            services.AddSingleton<IModelIngestRunner>(Runner);
            services.RemoveAll<IModelPreparer>();
            services.AddSingleton<IModelPreparer>(Preparer);
        });
    }

    public sealed class FakeRunner : IModelIngestRunner
    {
        public IngestRunResult Result { get; set; } = new(true, 3, 1, 2, null);
        public string? SeenName { get; private set; }
        public long SeenBytes { get; private set; }
        public int Calls { get; private set; }

        private readonly TaskCompletionSource _heldRunStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<IngestRunResult>? _heldRun;

        public Task HeldRunStarted => _heldRunStarted.Task;
        public bool HeldRunWasCancelled => _heldRun?.Task.IsCanceled ?? false;

        // A held run ends only when the service cancels it, as the ingest tool runs until it is stopped.
        public void HoldTheNextRunOpen() => _heldRun = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IngestRunResult> RunAsync(string ifcPath, string modelName, CancellationToken ct)
        {
            Calls++;
            SeenName = modelName;
            SeenBytes = new FileInfo(ifcPath).Length;
            if (_heldRun is not { } heldRun) return Task.FromResult(Result);

            ct.Register(() => heldRun.TrySetCanceled(ct));
            _heldRunStarted.TrySetResult();
            return heldRun.Task;
        }
    }

    public sealed class FakePreparer : IModelPreparer
    {
        public string? Error { get; set; }
        public int Calls { get; private set; }

        public Task<string?> ClearModelAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Error);
        }
    }
}
