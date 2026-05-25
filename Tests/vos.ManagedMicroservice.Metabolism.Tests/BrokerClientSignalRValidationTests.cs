using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using vos.ManagedMicroservice.Metabolism.Services;
using vos.ManagedMicroservice.Shared.Contracts.Validation;
using Xunit;

namespace vos.ManagedMicroservice.Metabolism.Tests;

/// <summary>
/// Phase 4 (Feature #5445): SignalR RelationshipPropertyChanged events are validated against
/// the relationship-property-changed-event schema before they reach simulation code. The
/// SignalR callback only fires from a real hub, so the validation logic is extracted into
/// an internal RaiseRelationshipPropertyChanged method that tests call directly via
/// InternalsVisibleTo.
/// </summary>
public class BrokerClientSignalRValidationTests
{
    private const string EventSchemaId = "https://villageos/contracts/relationship-property-changed-event.schema.json";

    [Fact]
    public void RaiseRelationshipPropertyChanged_ValidShape_ThrowMode_RaisesEvent()
    {
        var client = NewClient(SchemaViolationMode.Throw);
        var captured = (Guid: Guid.Empty, Prop: "", Value: (object?)null);
        client.OnRelationshipPropertyChanged += (id, name, value) => captured = (id, name, value);

        var rel = Guid.NewGuid();
        client.RaiseRelationshipPropertyChanged(rel, "amount", 5.0m);

        captured.Guid.Should().Be(rel);
        captured.Prop.Should().Be("amount");
        captured.Value.Should().Be(5.0m);
    }

    [Fact]
    public void RaiseRelationshipPropertyChanged_EmptyPropertyName_ThrowMode_ThrowsAndDoesNotRaise()
    {
        var client = NewClient(SchemaViolationMode.Throw);
        var raised = false;
        client.OnRelationshipPropertyChanged += (_, _, _) => raised = true;

        var act = () => client.RaiseRelationshipPropertyChanged(Guid.NewGuid(), "", 5.0m);

        var ex = act.Should().Throw<ContractValidationException>().Which;
        ex.Message.Should().Contain(EventSchemaId);
        raised.Should().BeFalse("validation must fail-fast before subscribers see the event");
    }

    [Fact]
    public void RaiseRelationshipPropertyChanged_EmptyPropertyName_LogMode_LogsAndStillRaises()
    {
        var logger = new RecordingLogger<BrokerClient>();
        var client = NewClient(SchemaViolationMode.Log, logger);
        var raised = false;
        client.OnRelationshipPropertyChanged += (_, _, _) => raised = true;

        client.RaiseRelationshipPropertyChanged(Guid.NewGuid(), "", 5.0m);

        logger.Warnings.Should().ContainSingle().Which.Should().Contain(EventSchemaId);
        raised.Should().BeTrue("Log mode never blocks consumers; simulation engine must keep ticking");
    }

    // ---- helpers ----

    private static TestableMetabolismBrokerClient NewClient(SchemaViolationMode mode, ILogger<BrokerClient>? logger = null)
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return new TestableMetabolismBrokerClient(
            httpFactory.Object,
            logger ?? new Mock<ILogger<BrokerClient>>().Object,
            "http://test", "consumes")
        {
            ViolationModeForTests = mode
        };
    }

    private sealed class TestableMetabolismBrokerClient : BrokerClient
    {
        public TestableMetabolismBrokerClient(IHttpClientFactory http, ILogger<BrokerClient> log, string url, string mode)
            : base(http, log, url, mode) { }

        public SchemaViolationMode? ViolationModeForTests { get; set; }
        protected override SchemaViolationMode OutboundViolationMode =>
            ViolationModeForTests ?? base.OutboundViolationMode;
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
