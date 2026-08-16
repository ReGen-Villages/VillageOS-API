using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared.Subscriptions;
using vos.Service.Tributary.Helpers;
using Xunit;

namespace vos.Service.Tributary.Tests;

// The kind edges sit on the template a registration `is`, and the selector applies traverse rules
// before it closes over `is` ancestors. These pin both halves: the selector asked for, and the
// walk that reads the answer back out of the snapshot it returns.
public class EndpointKindResolverTests
{
    private static readonly Guid Registration = Guid.NewGuid();
    private static readonly Guid Template = Guid.NewGuid();
    private static readonly Guid RootTemplate = Guid.NewGuid();
    private static readonly Guid IsPredicate = Guid.NewGuid();
    private static readonly Guid AuthRole = Guid.NewGuid();
    private static readonly Guid TokenExchange = Guid.NewGuid();

    private static SnapshotThing Thing(Guid id, string name, params string[] properties) =>
        new(id, name, false,
            properties.ToDictionary(p => p, _ => new SnapshotProperty(default, null, null)),
            new Dictionary<string, InheritedPropertySet>(), [], []);

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) =>
        new(Guid.NewGuid(), null, subject, predicate, target,
            new Dictionary<string, SnapshotProperty>(), new Dictionary<string, InheritedPropertySet>(), []);

    private static SnapshotDocument Snapshot() => new(0,
        [
            Thing(Registration, "County Parcels"),
            Thing(Template, "EsriEndpoint"),
            Thing(RootTemplate, "Endpoint"),
            Thing(IsPredicate, "is"),
            Thing(AuthRole, "authenticatesBy"),
            Thing(TokenExchange, "TokenExchangeAuth", "tokenUrl", "tokenPath"),
        ],
        [
            Edge(Registration, IsPredicate, Template),
            Edge(Template, IsPredicate, RootTemplate),
            Edge(Template, AuthRole, TokenExchange),
        ]);

    [Fact]
    public void SelectorFor_WalksIsBeforeTheRoles_OrTheKindOnTheTemplateIsNeverReached()
    {
        var selector = EndpointKindResolver.SelectorFor(Registration);

        selector.Ids.Should().Equal(Registration);
        selector.IncludeRelationships.Should().BeTrue();
        selector.Traverse![0].Predicate.Should().Be("is");
        selector.Traverse.Select(rule => rule.Predicate).Should().Contain(EndpointKindRoles.All);
    }

    [Fact]
    public void Resolve_KindDeclaredOnTheTemplate_IsReachedFromTheRegistration()
    {
        var resolved = EndpointKindResolver.Resolve(Snapshot(), Registration);

        resolved[EndpointKindRoles.Authentication].Name.Should().Be("TokenExchangeAuth");
    }

    [Fact]
    public void Resolve_TheKindsRequirements_ComeFromThePropertiesItDeclares()
    {
        EndpointKindResolver.Resolve(Snapshot(), Registration)[EndpointKindRoles.Authentication]
            .Requires.Should().BeEquivalentTo("tokenUrl", "tokenPath");
    }

    [Fact]
    public void Resolve_RoleNoKindIsReachedFor_IsAbsentRatherThanEmpty()
    {
        // Reaching nothing and reaching a kind that requires nothing are different answers, and only
        // the second is a decision the model made.
        EndpointKindResolver.Resolve(Snapshot(), Registration)
            .Should().NotContainKey(EndpointKindRoles.Paging);
    }

    [Fact]
    public void Resolve_RegistrationDeclaringItsOwnKind_BeatsTheTemplates()
    {
        var overridden = Guid.NewGuid();
        var snapshot = Snapshot();
        snapshot.Things.Add(Thing(overridden, "NoAuth"));
        snapshot.Relationships.Insert(0, Edge(Registration, AuthRole, overridden));

        EndpointKindResolver.Resolve(snapshot, Registration)[EndpointKindRoles.Authentication]
            .Name.Should().Be("NoAuth");
    }

    [Fact]
    public void MissingRequirements_EndpointSupplyingEverything_NamesNothing()
    {
        var kind = new ResolvedKind("TokenExchangeAuth", ["tokenUrl", "tokenPath"]);

        EndpointKindResolver.MissingRequirements(kind, Effective(
            ("tokenUrl", "https://example.test/token"), ("tokenPath", "access_token")))
            .Should().BeEmpty();
    }

    [Fact]
    public void MissingRequirements_KeyDeclaredButLeftBlank_CountsAsNotSupplied()
    {
        var kind = new ResolvedKind("TokenExchangeAuth", ["tokenUrl", "tokenPath"]);

        EndpointKindResolver.MissingRequirements(kind, Effective(
            ("tokenUrl", "  "), ("tokenPath", "access_token")))
            .Should().Equal("tokenUrl");
    }

    [Fact]
    public void MissingRequirements_NoKindReached_NamesNothing()
    {
        EndpointKindResolver.MissingRequirements(null, Effective()).Should().BeEmpty();
    }

    private static Dictionary<string, JsonElement> Effective(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(
            entry => entry.Key,
            entry => JsonDocument.Parse($"\"{entry.Value}\"").RootElement,
            StringComparer.OrdinalIgnoreCase);
}
