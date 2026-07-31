using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using vos.Service.Metabolism.Services;
using vos.Service.Shared.Contracts.Validation;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Metabolism.Tests;

// Phase 4 (Feature #5445): ApplyQuantityAsync + IncrementRelationshipPropertyAsync now
// validate their outbound payloads against apply-quantity-request /
// relationship-property-increment-request before POSTing. Same Throw/Log policy as
// Phase 3, switched via OutboundViolationMode -- tests override it on a thin subclass
// so both paths run regardless of build config.
public class MyceliumClientValidationTests
{
    private const string ApplyQuantitySchemaId = "https://villageos/contracts/apply-quantity-request.schema.json";
    private const string RelationshipIncrementSchemaId = "https://villageos/contracts/relationship-property-increment-request.schema.json";

    // ---- ApplyQuantityAsync ----

    [Fact]
    public async Task ApplyQuantityAsync_ValidPayload_ThrowMode_PostsAndReturns()
    {
        var (client, mock) = CreateTestClient(SchemaViolationMode.Throw);

        var result = await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m, "House-A", "kWh");

        result.Should().NotBeNull();
        mock.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath.Contains("/properties/quantity/"));
    }

    [Fact]
    public async Task ApplyQuantityAsync_InvalidPayload_ThrowMode_ThrowsAndDoesNotPost()
    {
        var (client, mock) = CreateTestClient(SchemaViolationMode.Throw,
            applyPayloadOverride: () => new { amount = 5m, subjectName = "x", unit = "kWh", rogue = "field" });

        var act = async () => await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m, "x", "kWh");

        var ex = (await act.Should().ThrowAsync<ContractValidationException>()).Which;
        ex.Message.Should().Contain(ApplyQuantitySchemaId);
        mock.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath.Contains("/properties/quantity/"));
    }

    [Fact]
    public async Task ApplyQuantityAsync_InvalidPayload_LogMode_LogsAndStillPosts()
    {
        var logger = new RecordingLogger<MyceliumClient>();
        var (client, mock) = CreateTestClient(SchemaViolationMode.Log,
            applyPayloadOverride: () => new { amount = 5m, subjectName = "x", unit = "kWh", rogue = "field" },
            logger: logger);

        await client.ApplyQuantityAsync("thing-1", "quantity", 5.0m, "x", "kWh");

        logger.Warnings.Should().ContainSingle().Which.Should().Contain(ApplyQuantitySchemaId);
        mock.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath.Contains("/properties/quantity/"));
    }

    // ---- IncrementRelationshipPropertyAsync ----

    [Fact]
    public async Task IncrementRelationshipPropertyAsync_ValidPayload_ThrowMode_Posts()
    {
        var (client, mock) = CreateTestClient(SchemaViolationMode.Throw);

        await client.IncrementRelationshipPropertyAsync("rel-1", "total", 2.0m);

        mock.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath.Contains("/increments"));
    }

    [Fact]
    public async Task IncrementRelationshipPropertyAsync_InvalidPayload_ThrowMode_ThrowsAndDoesNotPost()
    {
        var (client, mock) = CreateTestClient(SchemaViolationMode.Throw,
            incrementPayloadOverride: () => new { wrong = "shape" });

        var act = async () => await client.IncrementRelationshipPropertyAsync("rel-1", "total", 2.0m);

        var ex = (await act.Should().ThrowAsync<ContractValidationException>()).Which;
        ex.Message.Should().Contain(RelationshipIncrementSchemaId);
        mock.Requests.Should().NotContain(r => r.RequestUri!.AbsolutePath.Contains("/increments"));
    }

    [Fact]
    public async Task IncrementRelationshipPropertyAsync_InvalidPayload_LogMode_LogsAndStillPosts()
    {
        var logger = new RecordingLogger<MyceliumClient>();
        var (client, mock) = CreateTestClient(SchemaViolationMode.Log,
            incrementPayloadOverride: () => new { wrong = "shape" },
            logger: logger);

        await client.IncrementRelationshipPropertyAsync("rel-1", "total", 2.0m);

        logger.Warnings.Should().ContainSingle().Which.Should().Contain(RelationshipIncrementSchemaId);
        mock.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath.Contains("/increments"));
    }

    // ---- Helpers ----

    private static (TestableMetabolismMyceliumClient client, MockHttpMessageHandler mock) CreateTestClient(
        SchemaViolationMode mode,
        Func<object>? applyPayloadOverride = null,
        Func<object>? incrementPayloadOverride = null,
        ILogger<MyceliumClient>? logger = null)
    {
        var mock = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath == "/api/auth/token")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"token\":\"fake-jwt\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() =>
            new HttpClient(mock, disposeHandler: false) { BaseAddress = new Uri("http://test-mycelium") });

        var client = new TestableMetabolismMyceliumClient(
            httpFactory.Object,
            logger ?? new Mock<ILogger<MyceliumClient>>().Object,
            "http://test-mycelium", "consumes")
        {
            ViolationModeForTests = mode,
            ApplyPayloadOverride = applyPayloadOverride,
            IncrementPayloadOverride = incrementPayloadOverride
        };
        return (client, mock);
    }

    // Subclass that exposes the OutboundViolationMode override + lets tests inject a
    // deliberately malformed payload object via overrideable build hooks. The hooks
    // are protected-virtual on the production MyceliumClient (Phase 4).
    private sealed class TestableMetabolismMyceliumClient : MyceliumClient
    {
        public TestableMetabolismMyceliumClient(IHttpClientFactory http, ILogger<MyceliumClient> log, string myceliumUrl, string mode)
            : base(http, log, myceliumUrl, mode) { }

        public SchemaViolationMode? ViolationModeForTests { get; set; }
        public Func<object>? ApplyPayloadOverride { get; set; }
        public Func<object>? IncrementPayloadOverride { get; set; }

        protected override SchemaViolationMode OutboundViolationMode =>
            ViolationModeForTests ?? base.OutboundViolationMode;

        protected override object BuildApplyQuantityPayload(decimal amount, string? subjectName, string? unit) =>
            ApplyPayloadOverride is null
                ? base.BuildApplyQuantityPayload(amount, subjectName, unit)
                : ApplyPayloadOverride();

        protected override object BuildIncrementRelationshipPayload(decimal amount) =>
            IncrementPayloadOverride is null
                ? base.BuildIncrementRelationshipPayload(amount)
                : IncrementPayloadOverride();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
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
