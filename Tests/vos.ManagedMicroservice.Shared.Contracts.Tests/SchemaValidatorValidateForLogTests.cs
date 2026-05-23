using FluentAssertions;
using Microsoft.Extensions.Logging;
using vos.ManagedMicroservice.Shared.Contracts.Validation;
using Xunit;

namespace vos.ManagedMicroservice.Shared.Contracts.Tests;

/// <summary>
/// Tests for the Phase 3 (Feature #5440) "log instead of throw" failure policy that
/// <see cref="SchemaValidator.ValidateForLog"/> implements. Pairs with the existing
/// <see cref="SchemaValidator.ValidateOrThrow"/> -- one explicit policy per method, so
/// callers (<c>BrokerClientBase</c>) can pick via <c>#if DEBUG</c> and both paths stay
/// unit-testable regardless of the test assembly's build configuration.
/// </summary>
public class SchemaValidatorValidateForLogTests
{
    private const string TokenResponseSchemaId = "https://villageos/contracts/token-response.schema.json";

    private static readonly SchemaRegistry Registry = new();
    private static readonly SchemaValidator Validator = new();

    [Fact]
    public void ValidateForLog_ValidJson_DoesNotLog_DoesNotThrow()
    {
        var logger = new RecordingLogger();
        var schema = Registry.Get(TokenResponseSchemaId);

        var act = () => Validator.ValidateForLog("{\"token\":\"abc.def.ghi\"}", schema, TokenResponseSchemaId, logger);

        act.Should().NotThrow();
        logger.Entries.Should().BeEmpty("valid payloads must not emit log entries");
    }

    [Fact]
    public void ValidateForLog_InvalidJson_LogsSingleWarning_DoesNotThrow()
    {
        var logger = new RecordingLogger();
        var schema = Registry.Get(TokenResponseSchemaId);

        var act = () => Validator.ValidateForLog("{\"wrong\":\"shape\"}", schema, TokenResponseSchemaId, logger);

        act.Should().NotThrow("Release-build policy never blocks the caller");
        logger.Entries.Should().HaveCount(1, "one warning per call -- not one per error");
        var entry = logger.Entries[0];
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(TokenResponseSchemaId, "schemaId must be in the message");
    }

    [Fact]
    public void ValidateForLog_InvalidJson_LogMessageContainsFirstErrorPathCodeAndMessage()
    {
        var logger = new RecordingLogger();
        var schema = Registry.Get(TokenResponseSchemaId);

        // Token missing -> error code "Required" at path "#/token"
        Validator.ValidateForLog("{}", schema, TokenResponseSchemaId, logger);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Message.Should().Contain("Required", "the normalized error code must surface in the warning");
        entry.Message.Should().Contain("token", "the failing path must surface in the warning");
    }

    [Fact]
    public void ValidateForLog_NullLogger_Throws()
    {
        var schema = Registry.Get(TokenResponseSchemaId);

        var act = () => Validator.ValidateForLog("{\"token\":\"x\"}", schema, TokenResponseSchemaId, logger: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class RecordingLogger : ILogger
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
