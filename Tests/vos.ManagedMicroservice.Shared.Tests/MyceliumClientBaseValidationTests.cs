using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using vos.ManagedMicroservice.Shared.Contracts.Validation;
using vos.Tests.Shared;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Tests;

/// <summary>
/// Phase 3 (Feature #5440): outbound + response validation in <see cref="MyceliumClientBase"/>.
/// Both Throw and Log policies tested regardless of build config -- the MyceliumClient's
/// <c>OutboundViolationMode</c> is a virtual property the test subclass overrides, so
/// CI (Release) can exercise both paths without re-running tests in two configurations.
/// </summary>
public class MyceliumClientBaseValidationTests
{
    private const string MyceliumUrl = "http://localhost:7243";
    private const string TestToken = "service-token-abc";
    private const string TokenResponseSchemaId = "https://villageos/contracts/token-response.schema.json";
    private const string MyceliumRegisterRequestSchemaId = "https://villageos/contracts/mycelium-register-request.schema.json";

    // ---- RegisterAsync ----

    [Fact]
    public async Task RegisterAsync_ValidPayload_ThrowMode_PostsAndReturnsTrue()
    {
        var (client, _) = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.OK),
            serviceToken: TestToken, mode: SchemaViolationMode.Throw);

        var result = await client.RegisterAsync(7100, "Echo", "endpoint-service");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterAsync_InvalidPayload_ThrowMode_ThrowsContractValidationException_DoesNotPost()
    {
        var myceliumCalls = 0;
        var (client, _) = BuildClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/mycelium/register") myceliumCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, serviceToken: TestToken, mode: SchemaViolationMode.Throw);

        // Empty serviceName violates mycelium-register-request minLength:1
        var act = async () => await client.RegisterAsync(7100, "", "endpoint-service");

        var ex = (await act.Should().ThrowAsync<ContractValidationException>()).Which;
        ex.Message.Should().Contain(MyceliumRegisterRequestSchemaId);
        myceliumCalls.Should().Be(0, "validation must fail-fast before the POST");
    }

    [Fact]
    public async Task RegisterAsync_InvalidPayload_LogMode_LogsWarning_StillPosts()
    {
        var logger = new RecordingLogger();
        var myceliumCalls = 0;
        var (client, _) = BuildClient(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/mycelium/register") myceliumCalls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, serviceToken: TestToken, mode: SchemaViolationMode.Log, logger: logger);

        var result = await client.RegisterAsync(7100, "", "endpoint-service");

        result.Should().BeTrue("Log mode never blocks the call; mycelium decides on the body");
        myceliumCalls.Should().Be(1, "Log mode lets the malformed request through to Mycelium");
        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(MyceliumRegisterRequestSchemaId);
    }

    // ---- GetTokenAsync ----

    [Fact]
    public async Task GetTokenAsync_MyceliumReturnsValidShape_ThrowMode_ReturnsToken()
    {
        var (client, _) = BuildClient(_ => JsonResponse("""{"token":"from-mycelium"}"""),
            serviceToken: null, mode: SchemaViolationMode.Throw);

        var token = await client.GetTokenAsync();

        token.Should().Be("from-mycelium");
    }

    [Fact]
    public async Task GetTokenAsync_MyceliumReturnsInvalidShape_ThrowMode_ThrowsContractValidationException()
    {
        var (client, _) = BuildClient(_ => JsonResponse("""{"wrong":"shape"}"""),
            serviceToken: null, mode: SchemaViolationMode.Throw);

        var act = async () => await client.GetTokenAsync();

        var ex = (await act.Should().ThrowAsync<ContractValidationException>()).Which;
        ex.Message.Should().Contain(TokenResponseSchemaId);
    }

    [Fact]
    public async Task GetTokenAsync_MyceliumReturnsInvalidShape_LogMode_LogsAndReturnsNull()
    {
        var logger = new RecordingLogger();
        var (client, _) = BuildClient(_ => JsonResponse("""{"wrong":"shape"}"""),
            serviceToken: null, mode: SchemaViolationMode.Log, logger: logger);

        var token = await client.GetTokenAsync();

        token.Should().BeNull("token field missing -- existing catch returns null after log");
        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(TokenResponseSchemaId);
    }

    // ---- Helpers ----

    private static (TestableMyceliumClient client, MockHttpMessageHandler handler) BuildClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? serviceToken,
        SchemaViolationMode mode,
        ILogger? logger = null)
    {
        var handler = new MockHttpMessageHandler(respond);
        var httpClient = new HttpClient(handler);
        var factory = new TestHttpClientFactory(httpClient);
        var client = new TestableMyceliumClient(factory, logger ?? NullLogger.Instance, MyceliumUrl, serviceToken)
        {
            ViolationModeForTests = mode
        };
        return (client, handler);
    }

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingLogger : ILogger
    {
        public readonly List<string> Warnings = new();
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Warning) Warnings.Add(formatter(state, ex));
        }
    }
}
