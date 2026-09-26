using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using vos.Service.Feedback;
using vos.Tests.Shared;

namespace vos.Service.Feedback.Tests;

// Stands the relay up between a platform and an Azure DevOps organisation that are both answered here.
// The platform accepts exactly the tokens in AcceptedTokens; DevOps answers every upload and creation
// unless a test says otherwise, and every request either receives is kept for the test to read.
public sealed class FeedbackWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string PlatformAddress = "http://platform.test";
    public const string Organisation = "https://dev.azure.test/Example";
    public const string AccessToken = "devops-access-token";
    public const string ModelName = "Willow Bend";
    public const int CreatedWorkItem = 7400;

    public static readonly Guid ModelId = Guid.Parse("4d6c0f3e-9a55-4b5e-9d0e-7b1a3c2f5e11");

    private readonly string _destinationsFile = Path.Combine(Path.GetTempPath(), $"feedback-destinations-{Guid.NewGuid():N}.json");
    private readonly List<RecordedRequest> _platformRequests = [];
    private readonly List<RecordedRequest> _devOpsRequests = [];
    private readonly Lock _recording = new();

    public FeedbackWebApplicationFactory()
    {
        File.WriteAllText(_destinationsFile, """
            {
              "Trellis": {
                "project": "Clients",
                "areaPath": "Clients\\Console",
                "bugType": "Bug",
                "ideaType": "User Story",
                "tags": ["Console"]
              }
            }
            """);
    }

    public HashSet<string> AcceptedTokens { get; } = [];

    public bool PlatformUnreachable { get; set; }

    public Func<RecordedRequest, HttpResponseMessage?> DevOpsAnswers { get; set; } = _ => null;

    public string? AllowedOrigin { get; set; }

    public CapturingLogger<ReportFiling> Log { get; } = new();

    public IReadOnlyList<RecordedRequest> PlatformRequests
    {
        get { lock (_recording) return [.. _platformRequests]; }
    }

    public IReadOnlyList<RecordedRequest> DevOpsRequests
    {
        get { lock (_recording) return [.. _devOpsRequests]; }
    }

    public RecordedRequest WorkItemCreation => DevOpsRequests.Single(request => request.Path.Contains("/_apis/wit/workitems/"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Port", "5000");
        builder.UseSetting("MyceliumUrl", PlatformAddress);
        builder.UseSetting("DevOpsOrganization", Organisation);
        builder.UseSetting("DevOpsAccessToken", AccessToken);
        builder.UseSetting("Destinations", _destinationsFile);
        if (AllowedOrigin != null)
            builder.UseSetting("AllowedOrigin", AllowedOrigin);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new PerCallHttpClientFactory(new MockHttpMessageHandler(Answer)));
            services.AddSingleton<ILogger<ReportFiling>>(Log);
            services.AddSingleton<IStartupFilter, ArrivingThroughTheProxy>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_destinationsFile);
    }

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var recorded = RecordedRequest.From(request);
        if (request.RequestUri!.GetLeftPart(UriPartial.Authority) == PlatformAddress)
        {
            lock (_recording) _platformRequests.Add(recorded);
            return AnswerAsThePlatform(recorded);
        }

        lock (_recording) _devOpsRequests.Add(recorded);
        return DevOpsAnswers(recorded) ?? AnswerAsDevOps(recorded);
    }

    private HttpResponseMessage AnswerAsThePlatform(RecordedRequest request)
    {
        if (PlatformUnreachable)
            throw new HttpRequestException("Connection refused");
        if (request.Path != "/api/models" || request.Bearer is null || !AcceptedTokens.Contains(request.Bearer))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        return Json(HttpStatusCode.OK, new[] { new { Id = ModelId, Name = ModelName } });
    }

    private static HttpResponseMessage AnswerAsDevOps(RecordedRequest request)
    {
        if (request.Path.EndsWith("/_apis/wit/attachments", StringComparison.Ordinal))
            return Json(HttpStatusCode.Created, new { id = "a1", url = $"{Organisation}/_apis/wit/attachments/a1" });
        if (request.Path.Contains("/_apis/wit/workitems/", StringComparison.Ordinal))
            return Json(HttpStatusCode.OK, new { id = CreatedWorkItem });
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    // The test host opens no socket, so without this every caller reads as one address with nothing to
    // tell them apart by. Behind the reverse proxy the connection is from loopback.
    private sealed class ArrivingThroughTheProxy : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            builder =>
            {
                builder.Use(async (context, following) =>
                {
                    context.Connection.RemoteIpAddress = IPAddress.Loopback;
                    await following(context);
                });
                next(builder);
            };
    }
}

// Read while the request is still open, because the relay disposes its content once the call returns.
public sealed record RecordedRequest(
    HttpMethod Method, Uri Address, string? Bearer, string? Authorization, string? ContentType, byte[] Body)
{
    public string Path => Address.AbsolutePath;

    public string Text => Encoding.UTF8.GetString(Body);

    public static RecordedRequest From(HttpRequestMessage request)
    {
        var authorization = request.Headers.Authorization;
        return new RecordedRequest(
            request.Method,
            request.RequestUri!,
            authorization?.Scheme == "Bearer" ? authorization.Parameter : null,
            authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            request.Content?.ReadAsByteArrayAsync().GetAwaiter().GetResult() ?? []);
    }
}
