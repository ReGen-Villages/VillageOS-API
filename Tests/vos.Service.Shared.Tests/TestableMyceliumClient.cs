using Microsoft.Extensions.Logging;
using vos.Service.Shared.Contracts.Validation;

namespace vos.Service.Shared.Tests;

// Exposes the protected CreateAuthenticatedClientAsync so the base class's authenticated client can be
// tested directly, and lets OutboundViolationMode be set so validation tests check both the throwing and
// the logging path whatever the test assembly's build configuration.
internal sealed class TestableMyceliumClient : MyceliumClientBase
{
    public TestableMyceliumClient(
        IHttpClientFactory httpClientFactory, ILogger logger, string myceliumUrl, LaunchToken? launchToken = null,
        Func<Task<string?>>? tokenProvider = null, string? apiKey = null)
        : base(httpClientFactory, logger, myceliumUrl, launchToken, tokenProvider, apiKey)
    {
    }

    public SchemaViolationMode? ViolationModeForTests { get; set; }

    protected override SchemaViolationMode OutboundViolationMode =>
        ViolationModeForTests ?? base.OutboundViolationMode;

    public Task<HttpClient> CreateAuthenticatedClientPublicAsync(TimeSpan? timeout = null)
        => CreateAuthenticatedClientAsync(timeout);

    public Task<bool> PostRelationshipPublicAsync(Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken = default)
        => PostRelationshipAsync(subjectId, predicateId, targetId, TimeSpan.FromSeconds(5), cancellationToken);

    public Task<PlatformAnswer> AskForThingNamedPublicAsync(string name, TimeSpan? timeout = null)
        => AskForThingNamedAsync(name, timeout, CancellationToken.None);

    public Task<PlatformAnswer> AskToCreateThingPublicAsync(HttpContent thing)
        => AskToCreateThingAsync(thing, timeout: null, CancellationToken.None);

    public Task<PlatformAnswer> AskToDeleteThingPublicAsync(Guid thingId)
        => AskToDeleteThingAsync(thingId, timeout: null, CancellationToken.None);

    public Task<PlatformAnswer> AskToApplyFragmentPublicAsync(HttpContent fragment)
        => AskToApplyFragmentAsync(fragment, timeout: null, CancellationToken.None);

    public static Guid? IdentifierOfPublic(System.Text.Json.JsonElement thing) => IdentifierOf(thing);

    public static bool TryReadThingPublic(System.Text.Json.JsonElement thing, out Guid identifier, out string name)
        => TryReadThing(thing, out identifier, out name);
}
