using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using vos.Service.Delta.Models;
using vos.Service.Delta.Services;
using vos.Service.Delta.Tests.Services;
using vos.Service.Shared.Subscriptions;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Delta.Tests;

// Unit tests for TemplateCatalogProvisioner (Task #5468) — idempotent creation of one model's
// endpoint-template catalog and its is wiring. The provisioner is exercised
// against a real MyceliumClient over a MockHttpMessageHandler (the repo's
// mycelium-faking convention), with a small stateful MyceliumStub tracking created things
// and relationships so we can assert the exact mycelium writes.
public class TemplateCatalogProvisionerTests
{
    private const string RootOnlySeed = """
    {"things":[{"name":"Endpoint","properties":{"url":"","httpMethod":"GET"}}],"relationships":[]}
    """;

    private const string TwoLevelSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "", "httpMethod": "GET" } },
        { "name": "EsriEndpoint", "properties": { "layer": "" } }
      ],
      "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
    }
    """;

    private const string ThreeLevelSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "", "httpMethod": "GET" } },
        { "name": "EsriEndpoint", "properties": { "layer": "" } },
        { "name": "CountyParcels", "properties": { "layer": "3" } }
      ],
      "relationships": [
        { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" },
        { "subject": "CountyParcels", "predicate": "is", "target": "EsriEndpoint" }
      ]
    }
    """;

    // Two siblings, deliberately listed child-before-the-other to prove ordering is robust.
    private const string SiblingSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "" } },
        { "name": "B", "properties": {} },
        { "name": "A", "properties": {} }
      ],
      "relationships": [
        { "subject": "B", "predicate": "is", "target": "Endpoint" },
        { "subject": "A", "predicate": "is", "target": "Endpoint" }
      ]
    }
    """;

    // EsriEndpoint narrows requestContentType, which Endpoint already declares, and adds layer, which
    // it does not. The two halves must be provisioned differently.
    private const string NarrowingSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "", "requestContentType": "application/json" } },
        { "name": "EsriEndpoint", "properties": { "requestContentType": "application/x-www-form-urlencoded", "layer": "" } }
      ],
      "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" } ]
    }
    """;

    // A grouping template with `properties` absent rather than empty.
    private const string PropertylessChildSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "" } },
        { "name": "Grouping" }
      ],
      "relationships": [ { "subject": "Grouping", "predicate": "is", "target": "Endpoint" } ]
    }
    """;

    // The `is` target is authored in a different case than the thing name to pin case-insensitive
    // parent resolution during wiring.
    private const string CaseVariationSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "" } },
        { "name": "EsriEndpoint", "properties": { "layer": "" } }
      ],
      "relationships": [ { "subject": "EsriEndpoint", "predicate": "is", "target": "endpoint" } ]
    }
    """;

    // A template reaching a kind. TokenExchangeAuth declares the keys it requires of an endpoint, so
    // the provisioned Thing carries one property per requirement — the same way a template declares a
    // structural key, because that is what a requirement is.
    private const string KindSeed = """
    {
      "things": [
        { "name": "Endpoint", "properties": { "url": "" } },
        { "name": "EsriEndpoint", "properties": { "tokenPath": "token" } }
      ],
      "kinds": [
        { "name": "TokenExchangeAuth", "requires": ["tokenUrl", "tokenPath"] }
      ],
      "relationships": [
        { "subject": "EsriEndpoint", "predicate": "is", "target": "Endpoint" },
        { "subject": "EsriEndpoint", "predicate": "authenticatesBy", "target": "TokenExchangeAuth" }
      ]
    }
    """;

    [Fact]
    public async Task ProvisionAsync_SeedDeclaringAKind_CreatesTheKindThingCarryingWhatItRequires()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, KindSeed).ProvisionAsync();

        stub.CreatedByName.Should().ContainKey("TokenExchangeAuth");
        stub.ThingPostBodies.Should().Contain(b => b.Contains("TokenExchangeAuth") && b.Contains("tokenUrl"));
    }

    [Fact]
    public async Task ProvisionAsync_SeedDeclaringAKind_MintsTheRolePredicateAndWiresTheEdge()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, KindSeed).ProvisionAsync();

        stub.CreatedByName.Should().ContainKey("authenticatesBy");
        stub.Relationships.Should().Contain(r =>
            r.Subject == stub.CreatedByName["EsriEndpoint"]
            && r.Predicate == stub.CreatedByName["authenticatesBy"]
            && r.Target == stub.CreatedByName["TokenExchangeAuth"]);
    }

    [Fact]
    public async Task ProvisionAsync_KindAndRolePredicateAlreadyPresent_ReusesThemRatherThanMintingASecond()
    {
        var stub = new MyceliumStub();
        var kindId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        stub.Preexist("TokenExchangeAuth", kindId);
        stub.Preexist("authenticatesBy", roleId);

        await Provisioner(stub, KindSeed).ProvisionAsync();

        stub.CreatedByName.Should().NotContainKey("TokenExchangeAuth");
        stub.CreatedByName.Should().NotContainKey("authenticatesBy");
        stub.Relationships.Should().Contain(r => r.Predicate == roleId && r.Target == kindId);
    }

    [Fact]
    public async Task ProvisionAsync_KindThingCannotBeCreated_LeavesTheEdgeUnwiredRatherThanPointingAtNothing()
    {
        var stub = new MyceliumStub { FailCreateName = "TokenExchangeAuth" };

        await Provisioner(stub, KindSeed).ProvisionAsync();

        stub.Relationships.Should().NotContain(r => r.Predicate == stub.CreatedByName.GetValueOrDefault("authenticatesBy"));
    }

    [Fact]
    public async Task ProvisionAsync_RolePredicateCannotBeMinted_LeavesTheEdgeUnwired()
    {
        var stub = new MyceliumStub { FailCreateName = "authenticatesBy" };

        await Provisioner(stub, KindSeed).ProvisionAsync();

        stub.CreatedByName.Should().ContainKey("TokenExchangeAuth", "the kind itself is unaffected");
        stub.Relationships.Should().NotContain(r => r.Target == stub.CreatedByName["TokenExchangeAuth"]);
    }

    [Fact]
    public async Task ProvisionAsync_KindEdgeWiredAfterTheIsEdge_SoTheTemplateIsAlreadyInItsChain()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, KindSeed).ProvisionAsync();

        var isEdge = stub.Calls.IndexOf("relationship:EsriEndpoint->Endpoint");
        var kindEdge = stub.Calls.IndexOf("relationship:EsriEndpoint->TokenExchangeAuth");
        isEdge.Should().BeGreaterThanOrEqualTo(0);
        kindEdge.Should().BeGreaterThan(isEdge);
    }

    [Fact]
    public async Task ProvisionAsync_FreshMycelium_CreatesEveryTemplateThingWithProperties()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "EsriEndpoint" });
        stub.ThingPostBodies.Should().Contain(b => b.Contains("httpMethod") && b.Contains("GET"),
            "the Endpoint template thing is created carrying its seed properties");
    }

    [Fact]
    public async Task ProvisionAsync_FreshMycelium_WiresChildToParentViaIs()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.Relationships.Should().ContainSingle();
        var rel = stub.Relationships[0];
        rel.Predicate.Should().Be(stub.IsId);
        rel.Subject.Should().Be(stub.CreatedByName["EsriEndpoint"]);
        rel.Target.Should().Be(stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_RootOnly_CreatesThingButNoRelationship()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, RootOnlySeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint" });
        stub.Relationships.Should().BeEmpty("the root template has no parent to wire to");
    }

    [Fact]
    public async Task ProvisionAsync_AllTemplatesAlreadyExistAndAreWired_CreatesNothingAndWiresNothing()
    {
        var stub = new MyceliumStub();
        var endpointId = Guid.NewGuid();
        var esriId = Guid.NewGuid();
        stub.Preexist("Endpoint", endpointId);
        stub.Preexist("EsriEndpoint", esriId);
        stub.PreexistEdge(esriId, stub.IsId, endpointId);

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.ThingPostCount.Should().Be(0, "find-or-create is idempotent across restarts");
        stub.Relationships.Should().BeEmpty("an edge the model already carries is not written a second time");
    }

    [Fact]
    public async Task ProvisionAsync_ParentExistsChildMissing_WiresNewChildToExistingParent()
    {
        var stub = new MyceliumStub();
        var endpointId = Guid.NewGuid();
        stub.Preexist("Endpoint", endpointId);

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "EsriEndpoint" });
        stub.Relationships.Should().ContainSingle();
        stub.Relationships[0].Subject.Should().Be(stub.CreatedByName["EsriEndpoint"]);
        stub.Relationships[0].Target.Should().Be(endpointId);
    }

    [Fact]
    public async Task ProvisionAsync_IsPredicateMissing_AbortsWithoutCreatingAnything()
    {
        var stub = new MyceliumStub { IsPredicatePresent = false };

        var act = async () => await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        await act.Should().NotThrowAsync();
        stub.ThingPostCount.Should().Be(0);
        stub.Relationships.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionAsync_ThreeLevelChain_WiresEachToDirectParentOnly()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, ThreeLevelSeed).ProvisionAsync();

        stub.Relationships.Should().HaveCount(2);
        stub.Relationships.Should().Contain(r =>
            r.Subject == stub.CreatedByName["EsriEndpoint"] && r.Target == stub.CreatedByName["Endpoint"]);
        stub.Relationships.Should().Contain(r =>
            r.Subject == stub.CreatedByName["CountyParcels"] && r.Target == stub.CreatedByName["EsriEndpoint"]);
        stub.Relationships.Should().NotContain(r =>
            r.Subject == stub.CreatedByName["CountyParcels"] && r.Target == stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_SiblingsAtSameDepth_AllCreatedAndWiredRegardlessOfSeedOrder()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, SiblingSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "A", "B" });
        stub.Relationships.Should().HaveCount(2);
        stub.Relationships.Should().Contain(r => r.Subject == stub.CreatedByName["A"] && r.Target == stub.CreatedByName["Endpoint"]);
        stub.Relationships.Should().Contain(r => r.Subject == stub.CreatedByName["B"] && r.Target == stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_ParentNameCaseVariation_WiresViaCaseInsensitiveLookup()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, CaseVariationSeed).ProvisionAsync();

        stub.Relationships.Should().ContainSingle();
        stub.Relationships[0].Subject.Should().Be(stub.CreatedByName["EsriEndpoint"]);
        stub.Relationships[0].Target.Should().Be(stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_TemplateCreateFails_SkipsItAndDoesNotWireDescendants()
    {
        var stub = new MyceliumStub { FailCreateName = "Endpoint" };

        await Provisioner(stub, TwoLevelSeed).ProvisionAsync();

        stub.ThingPostCount.Should().Be(2, "both creates are attempted; the loop continues past a failure");
        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "EsriEndpoint" });
        stub.Relationships.Should().BeEmpty("the child cannot be wired to a parent that failed to create");
    }

    // A key the parent chain already declares must not be created as an own property: the thing would
    // then own a name it also inherits, which the platform forbids (invariant I1/I2) and which makes
    // the key surface twice in the resolved view. It has to be written after `is` exists, so Mycelium
    // stores it as an override.

    [Fact]
    public async Task ProvisionAsync_TemplateNarrowsAParentKey_DoesNotCreateItAsAnOwnProperty()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, NarrowingSeed).ProvisionAsync();

        var esriCreateBody = stub.ThingPostBodies.Single(b => b.Contains("EsriEndpoint"));
        esriCreateBody.Should().NotContain("requestContentType", "the parent declares it, so creating it here would shadow");
        esriCreateBody.Should().Contain("layer", "a key no ancestor declares is genuinely this template's own");
    }

    [Fact]
    public async Task ProvisionAsync_TemplateNarrowsAParentKey_WritesItAfterTheIsEdgeExists()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, NarrowingSeed).ProvisionAsync();

        stub.PropertyWrites.Should().ContainSingle()
            .Which.Should().Match<(Guid ThingId, string Name, string Body)>(w =>
                w.ThingId == stub.CreatedByName["EsriEndpoint"]
                && w.Name == "requestContentType"
                && w.Body.Contains("application/x-www-form-urlencoded"));

        stub.Calls.IndexOf("property:EsriEndpoint.requestContentType")
            .Should().BeGreaterThan(stub.Calls.IndexOf("relationship:EsriEndpoint->Endpoint"));
    }

    [Fact]
    public async Task ProvisionAsync_RootTemplate_KeepsEveryPropertyOnCreate()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, NarrowingSeed).ProvisionAsync();

        var endpointCreateBody = stub.ThingPostBodies.Single(b => !b.Contains("EsriEndpoint"));
        endpointCreateBody.Should().Contain("requestContentType", "the root inherits nothing, so nothing can shadow");
        stub.PropertyWrites.Should().NotContain(w => w.ThingId == stub.CreatedByName["Endpoint"]);
    }

    [Fact]
    public async Task ProvisionAsync_ParentWiringFailed_SkipsTheNarrowedWriteRatherThanShadowing()
    {
        var stub = new MyceliumStub { FailRelationships = true };

        await Provisioner(stub, NarrowingSeed).ProvisionAsync();

        stub.PropertyWrites.Should().BeEmpty(
            "without the is edge the key is not inherited, so writing it would create the shadow this avoids");
    }

    // The run that fails the edge still leaves the thing behind, and a name lookup cannot tell that
    // thing from a finished one. Without repair the template answers with the parent's values for the
    // rest of the model's life, and only the run that first failed ever says so.
    [Fact]
    public async Task ProvisionAsync_EarlierRunLeftTheIsEdgeUnwired_WiresItAndThenWritesTheNarrowedKey()
    {
        var stub = new MyceliumStub { FailRelationships = true };
        await Provisioner(stub, NarrowingSeed).ProvisionAsync();
        stub.FailRelationships = false;

        await Provisioner(stub, NarrowingSeed).ProvisionAsync();

        stub.Relationships.Should().ContainSingle()
            .Which.Should().Match<(Guid Subject, Guid Predicate, Guid Target)>(r =>
                r.Subject == stub.CreatedByName["EsriEndpoint"]
                && r.Predicate == stub.IsId
                && r.Target == stub.CreatedByName["Endpoint"]);
        stub.PropertyWrites.Should().ContainSingle()
            .Which.Should().Match<(Guid ThingId, string Name, string Body)>(w =>
                w.ThingId == stub.CreatedByName["EsriEndpoint"] && w.Name == "requestContentType");
        stub.Calls.IndexOf("property:EsriEndpoint.requestContentType")
            .Should().BeGreaterThan(stub.Calls.IndexOf("relationship:EsriEndpoint->Endpoint"));
    }

    [Fact]
    public async Task ProvisionAsync_SecondRunOverAFinishedCatalogue_WritesNothing()
    {
        var stub = new MyceliumStub();
        await Provisioner(stub, KindSeed).ProvisionAsync();
        var afterFirstRun = stub.Calls.Count;
        var thingsAfterFirstRun = stub.ThingPostCount;

        await Provisioner(stub, KindSeed).ProvisionAsync();

        stub.Calls.Should().HaveCount(afterFirstRun, "every edge and every narrowed key is already there");
        stub.ThingPostCount.Should().Be(thingsAfterFirstRun);
    }

    [Fact]
    public async Task ProvisionAsync_TemplateDeclaringNoProperties_IsCreatedAndWired()
    {
        var stub = new MyceliumStub();

        await Provisioner(stub, PropertylessChildSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "Grouping" });
        stub.Relationships.Should().ContainSingle();
        stub.PropertyWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionAsync_NarrowedWriteFails_StillProvisionsTheRestOfTheCatalog()
    {
        var stub = new MyceliumStub { FailPropertyWrites = true };

        await Provisioner(stub, ThreeLevelSeed).ProvisionAsync();

        stub.CreatedByName.Keys.Should().BeEquivalentTo(new[] { "Endpoint", "EsriEndpoint", "CountyParcels" });
        stub.Relationships.Should().HaveCount(2, "a template that could not narrow a key is still wired");
    }

    // ---------- Harness ----------

    // Both clients share one handler, so the writes one makes are what the other's snapshot reports —
    // which is the whole subject of a repair run.
    private static TemplateCatalogProvisioner Provisioner(MyceliumStub stub, string seedJson)
    {
        var factory = new PerCallFactory(stub.Handler());
        return new(
            new MyceliumClient(factory, Substitute.For<ILogger<MyceliumClient>>(), "http://localhost", "test-token"),
            new SubscriptionClient(factory, Substitute.For<ILogger<SubscriptionClient>>(), "http://localhost", "test-token"),
            new InMemoryEndpointSeedProvider(seedJson).LoadGraph(),
            Substitute.For<ILogger<TemplateCatalogProvisioner>>());
    }

    private sealed class PerCallFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public PerCallFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // Stateful fake of Mycelium's thing/relationship endpoints used by the provisioner.
    private sealed class MyceliumStub
    {
        public Guid IsId { get; } = Guid.NewGuid();
        public bool IsPredicatePresent { get; init; } = true;
        public string? FailCreateName { get; init; }
        public bool FailRelationships { get; set; }
        public bool FailPropertyWrites { get; init; }

        private readonly Dictionary<string, Guid> _preexisting = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> CreatedByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ThingPostBodies { get; } = new();
        public List<(Guid Subject, Guid Predicate, Guid Target)> Relationships { get; } = new();
        public List<(Guid ThingId, string Name, string Body)> PropertyWrites { get; } = new();

        // Mycelium writes in call order, so ordering between the `is` edge and a property write is the
        // whole point of these tests; record it rather than inferring it from the per-kind lists.
        public List<string> Calls { get; } = new();
        public int ThingPostCount => ThingPostBodies.Count;

        // Kept apart from Relationships, which records only what this run wrote — the two must not
        // collapse, or a test asserting nothing was wired could not tell a skipped write from any write.
        private readonly List<(Guid Subject, Guid Predicate, Guid Target)> _preexistingEdges = new();

        public void Preexist(string name, Guid id) => _preexisting[name] = id;

        public void PreexistEdge(Guid subject, Guid predicate, Guid target) =>
            _preexistingEdges.Add((subject, predicate, target));

        public MockHttpMessageHandler Handler() => new(Route);

        private HttpResponseMessage Route(HttpRequestMessage req)
        {
            var path = req.RequestUri!.AbsolutePath;

            if (CatalogEdgeRead.Answer(req, _preexistingEdges.Concat(Relationships)) is { } catalogEdges)
                return catalogEdges;

            if (req.Method == HttpMethod.Get && path == "/api/things")
            {
                var name = NameParam(req);
                if (string.Equals(name, "is", StringComparison.OrdinalIgnoreCase))
                    return IsPredicatePresent ? Thing(IsId, "is") : Json("null");
                if (name != null && _preexisting.TryGetValue(name, out var id))
                    return Thing(id, name);
                if (name != null && CreatedByName.TryGetValue(name, out var created))
                    return Thing(created, name);
                return Json("null");
            }

            if (req.Method == HttpMethod.Post && path == "/api/things")
            {
                var body = Body(req);
                ThingPostBodies.Add(body);
                var name = StringProp(body, "name") ?? string.Empty;
                if (FailCreateName != null && string.Equals(name, FailCreateName, StringComparison.OrdinalIgnoreCase))
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                var newId = Guid.NewGuid();
                CreatedByName[name] = newId;
                return Thing(newId, name);
            }

            if (req.Method == HttpMethod.Post && path == "/api/relationships")
            {
                if (FailRelationships)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                var body = Body(req);
                var subject = GuidProp(body, "subjectId");
                var target = GuidProp(body, "targetId");
                Relationships.Add((subject, GuidProp(body, "predicateId"), target));
                Calls.Add($"relationship:{NameOf(subject)}->{NameOf(target)}");
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (req.Method == HttpMethod.Put && path.EndsWith("/properties", StringComparison.Ordinal))
            {
                if (FailPropertyWrites)
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                var body = Body(req);
                var thingId = Guid.Parse(path.Split('/')[^2]);
                var name = StringProp(body, "name") ?? string.Empty;
                PropertyWrites.Add((thingId, name, body));
                Calls.Add($"property:{NameOf(thingId)}.{name}");
                return Json("{}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string NameOf(Guid id)
        {
            foreach (var (name, known) in CreatedByName)
                if (known == id) return name;
            foreach (var (name, known) in _preexisting)
                if (known == id) return name;
            return id.ToString();
        }

        private static string Body(HttpRequestMessage req) =>
            req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;

        private static string? NameParam(HttpRequestMessage req)
        {
            foreach (var pair in req.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "name")
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        private static string? StringProp(string json, string prop)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
        }

        private static Guid GuidProp(string json, string prop)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(prop, out var v) && Guid.TryParse(v.GetString(), out var g)
                ? g : Guid.Empty;
        }

        private static HttpResponseMessage Thing(Guid id, string name) =>
            Json($"{{\"Id\":\"{id}\",\"Name\":\"{name}\",\"Properties\":{{}}}}");

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
