using System.Text.Json;
using FluentAssertions;
using vos.Service.Confluence.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Confluence.Tests.Helpers;

public class CoveringSourceResolverTests
{
    private static SnapshotThing Thing(Guid id, string name) => new(
        id, name, false,
        new Dictionary<string, SnapshotProperty>(),
        new Dictionary<string, InheritedPropertySet>(),
        Array.Empty<string>(),
        Array.Empty<Guid>());

    private static SnapshotThing Unnamed(Guid id) => new(
        id, null, false,
        new Dictionary<string, SnapshotProperty>(),
        new Dictionary<string, InheritedPropertySet>(),
        Array.Empty<string>(),
        Array.Empty<Guid>());

    private static SnapshotRelationship Edge(Guid subject, Guid predicate, Guid target) => new(
        Guid.NewGuid(), null, subject, predicate, target,
        new Dictionary<string, SnapshotProperty>(),
        new Dictionary<string, InheritedPropertySet>(),
        Array.Empty<string>());

    // One place to build a model, so a test reads as the shape it is asserting rather than as plumbing.
    private sealed class ModelBuilder
    {
        private readonly Dictionary<string, Guid> _ids = new(StringComparer.Ordinal);
        private readonly List<SnapshotThing> _things = new();
        private readonly List<SnapshotRelationship> _edges = new();

        public Guid Id(string name)
        {
            if (_ids.TryGetValue(name, out var existing)) return existing;
            var id = Guid.NewGuid();
            _ids[name] = id;
            _things.Add(Thing(id, name));
            return id;
        }

        public ModelBuilder Relate(string subject, string predicate, string target)
        {
            _edges.Add(Edge(Id(subject), Id(predicate), Id(target)));
            return this;
        }

        // Re-declares a Thing as a type, carrying the flag when it is one a reader finds by mark.
        public ModelBuilder Archetype(string name, string? flag = null)
        {
            var id = Id(name);
            var properties = flag == null
                ? new Dictionary<string, SnapshotProperty>()
                : new Dictionary<string, SnapshotProperty>
                {
                    [flag] = new(JsonDocument.Parse("true").RootElement, null, null),
                };

            _things.RemoveAll(thing => thing.Id == id);
            _things.Add(new SnapshotThing(
                id, name, true, properties,
                new Dictionary<string, InheritedPropertySet>(),
                Array.Empty<string>(), Array.Empty<Guid>()));
            return this;
        }

        public SnapshotDocument Build() => new(0, _things, _edges);
    }

    private static ModelBuilder StudyOf(string site) =>
        new ModelBuilder().Relate(site + "Study", CoveringSourceResolver.StudiesPredicate, site);

    // A connection the model marks as one a site analysis starts, bound to the service it dispatches.
    private static ModelBuilder MarkedConnection(ModelBuilder model, string connection, string prototype)
    {
        model.Relate(connection, CoveringSourceResolver.IsPredicateName, "SiteAnalysisConnection")
             .Relate(connection, CoveringSourceResolver.HasPredicate, connection + " service")
             .Relate(connection + " service", CoveringSourceResolver.IsPredicateName, prototype);
        model.Archetype("SiteAnalysisConnection", CoveringSourceResolver.SiteAnalysisConnectionFlag);
        return model.Archetype(prototype);
    }

    // A source is callable only through its registration, so every scenario below wires both.
    private static ModelBuilder SourceCovering(ModelBuilder model, string source, string place) =>
        model.Relate(source, CoveringSourceResolver.CoversPredicate, place)
             .Relate(source, CoveringSourceResolver.ResolvedByPredicate, source + "Endpoint");

    [Fact]
    public void Resolve_SourceCoveringTheSitesOwnPlace_IsSelected()
    {
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");
        SourceCovering(model, "NationalFloodPortal", "Portugal");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Select(source => source.Name).Should().Equal("NationalFloodPortal");
        covering[0].EndpointName.Should().Be("NationalFloodPortalEndpoint");
    }

    [Fact]
    public void Resolve_SourceCoveringAContainingPlace_IsSelected()
    {
        // Nesting is what lets a source cover every site under it without naming any of them.
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe")
            .Relate("Europe", CoveringSourceResolver.IsInPredicate, "Earth");
        SourceCovering(model, "OpenMeteo", "Earth");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Select(source => source.Name).Should().Equal("OpenMeteo");
    }

    [Fact]
    public void Resolve_SourceCoveringAPlaceTheSiteIsNotIn_IsNotSelected()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe");
        SourceCovering(model, "BrazilianPortal", "Brazil");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_SourceCoveringASiblingPlace_IsNotSelected()
    {
        // Spain sits under the same Europe the site reaches; covering a sibling is not covering the site.
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe")
            .Relate("Spain", CoveringSourceResolver.IsInPredicate, "Europe");
        SourceCovering(model, "SpanishPortal", "Spain");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_SourceCoveringSeveralPlacesTheSiteReaches_IsSelectedOnce()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe");
        SourceCovering(model, "Copernicus", "Portugal");
        model.Relate("Copernicus", CoveringSourceResolver.CoversPredicate, "Europe");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_SourceWithNoRegistration_IsLeftOut()
    {
        // Not a discovery that failed — a source that was never callable. Reporting it as an outage
        // would blame a provider for a gap in the model.
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Uncallable", CoveringSourceResolver.CoversPredicate, "Portugal");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_SiteInNoPlace_SelectsNothing()
    {
        var model = new ModelBuilder();
        model.Id("WillowBend");
        SourceCovering(model, "OpenMeteo", "Earth");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_EveryCoveringSource_IsReturned()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Earth");
        SourceCovering(model, "NationalFloodPortal", "Portugal");
        SourceCovering(model, "OpenMeteo", "Earth");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Select(source => source.Name).Should().BeEquivalentTo("NationalFloodPortal", "OpenMeteo");
    }

    [Fact]
    public void Resolve_PlaceNestingWithACycle_Terminates()
    {
        // The model should never hold one, but a resolver that hangs on bad data is worse than one
        // that answers from what it can reach.
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Portugal", CoveringSourceResolver.IsInPredicate, "Europe")
            .Relate("Europe", CoveringSourceResolver.IsInPredicate, "Portugal");
        SourceCovering(model, "Copernicus", "Europe");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Select(source => source.Name).Should().Equal("Copernicus");
    }

    [Fact]
    public void AnalysisOf_StudyAndAMarkedConnection_TriggersThatService()
    {
        var model = StudyOf("WillowBend");
        MarkedConnection(model, "balancesEnergy", "EnergyBalance prototype");

        var analysis = CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"));

        analysis!.StudyId.Should().Be(model.Id("WillowBendStudy"));
        analysis.Triggers.Should().ContainSingle()
            .Which.Should().Be(new AnalysisTrigger(
                "balancesEnergy", model.Id("balancesEnergy"), model.Id("EnergyBalance prototype")));
    }

    [Fact]
    public void AnalysisOf_SiteWithNoStudy_IsNull()
    {
        // Not a failure: nothing was ever going to compute, and reporting it as one would blame the run
        // for a gap in the model — the same rule a source with no registration is left out under.
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");
        MarkedConnection(model, "balancesEnergy", "EnergyBalance prototype");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend")).Should().BeNull();
    }

    [Fact]
    public void AnalysisOf_AnotherSitesStudy_IsNotReturned()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("ElsewhereStudy", CoveringSourceResolver.StudiesPredicate, "Elsewhere");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend")).Should().BeNull();
    }

    [Fact]
    public void AnalysisOf_ConnectionWithoutTheMark_IsNotATrigger()
    {
        // An unmarked connection is one bound for some other purpose. Starting it would dispatch a
        // service against a study it knows nothing about.
        var model = StudyOf("WillowBend")
            .Relate("ordinary", CoveringSourceResolver.HasPredicate, "ordinary service")
            .Relate("ordinary service", CoveringSourceResolver.IsPredicateName, "EnergyBalance prototype");
        model.Archetype("EnergyBalance prototype");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .Triggers.Should().BeEmpty();
    }

    [Fact]
    public void AnalysisOf_MarkedConnectionBindingNoService_IsNotATrigger()
    {
        // An edge pointing at nothing to dispatch would be written and never answered, which reads
        // afterwards as an analysis that started and produced nothing.
        var model = StudyOf("WillowBend")
            .Relate("balancesEnergy", CoveringSourceResolver.IsPredicateName, "SiteAnalysisConnection");
        model.Archetype("SiteAnalysisConnection", CoveringSourceResolver.SiteAnalysisConnectionFlag);

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .Triggers.Should().BeEmpty();
    }

    [Fact]
    public void AnalysisOf_ServiceTypedByAThingThatIsNotAnArchetype_IsNotATrigger()
    {
        // A service `is` its prototype, and a prototype is a type. Pointing the analysis edge at an
        // ordinary Thing that happens to sit on an `is` edge would dispatch against a member.
        var model = StudyOf("WillowBend")
            .Relate("balancesEnergy", CoveringSourceResolver.IsPredicateName, "SiteAnalysisConnection")
            .Relate("balancesEnergy", CoveringSourceResolver.HasPredicate, "balancesEnergy service")
            .Relate("balancesEnergy service", CoveringSourceResolver.IsPredicateName, "not a type");
        model.Archetype("SiteAnalysisConnection", CoveringSourceResolver.SiteAnalysisConnectionFlag);

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .Triggers.Should().BeEmpty();
    }

    [Fact]
    public void AnalysisOf_IsChainThatLoops_DoesNotHang()
    {
        // The walk climbs the is chain, and a model can be edited into a cycle. It has to terminate on
        // the path that serves a discovery run rather than spin.
        var model = StudyOf("WillowBend")
            .Relate("balancesEnergy", CoveringSourceResolver.IsPredicateName, "roundabout")
            .Relate("roundabout", CoveringSourceResolver.IsPredicateName, "balancesEnergy");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .Triggers.Should().BeEmpty();
    }

    [Fact]
    public void AnalysisOf_MarkThroughAnIntermediateArchetype_IsStillFound()
    {
        // A model may put its own archetype between a connection and the marked one. The mark is
        // inherited through the is chain, so the walk climbs rather than reading one step.
        var model = StudyOf("WillowBend")
            .Relate("balancesEnergy", CoveringSourceResolver.IsPredicateName, "PlatformAnalysisConnection")
            .Relate("PlatformAnalysisConnection", CoveringSourceResolver.IsPredicateName, "SiteAnalysisConnection")
            .Relate("balancesEnergy", CoveringSourceResolver.HasPredicate, "balancesEnergy service")
            .Relate("balancesEnergy service", CoveringSourceResolver.IsPredicateName, "EnergyBalance prototype");
        model.Archetype("PlatformAnalysisConnection");
        model.Archetype("SiteAnalysisConnection", CoveringSourceResolver.SiteAnalysisConnectionFlag);
        model.Archetype("EnergyBalance prototype");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .Triggers.Select(trigger => trigger.ConnectionName).Should().Equal("balancesEnergy");
    }

    [Fact]
    public void AnalysisOf_TwoMarkedConnections_AreOrderedByName()
    {
        // A run writes its edges the same way twice, which is what makes the log of two runs comparable.
        var model = StudyOf("WillowBend");
        MarkedConnection(model, "reservesWater", "WaterReserve prototype");
        MarkedConnection(model, "balancesEnergy", "EnergyBalance prototype");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .Triggers.Select(trigger => trigger.ConnectionName)
            .Should().Equal("balancesEnergy", "reservesWater");
    }

    [Fact]
    public void AnalysisOf_SnapshotHoldingAnUnnamedThing_StillFindsTheStudy()
    {
        // A snapshot Thing's name is optional, and this runs on the path that serves a discovery run:
        // one unnamed Thing anywhere in the snapshot must not stop the site's study being found.
        var site = Guid.NewGuid();
        var study = Guid.NewGuid();
        var studies = Guid.NewGuid();
        var snapshot = new SnapshotDocument(
            0,
            new List<SnapshotThing>
            {
                Thing(site, "WillowBend"),
                Thing(study, "WillowBendStudy"),
                Thing(studies, CoveringSourceResolver.StudiesPredicate),
                Unnamed(Guid.NewGuid()),
            },
            new List<SnapshotRelationship> { Edge(study, studies, site) });

        CoveringSourceResolver.AnalysisOf(snapshot, site)!.StudyId.Should().Be(study);
    }

    [Fact]
    public void AnalysisOf_PredicateNameMatchIsCaseInsensitive()
    {
        var model = new ModelBuilder().Relate("WillowBendStudy", "Studies", "WillowBend");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .StudyId.Should().Be(model.Id("WillowBendStudy"));
    }

    [Fact]
    public void SelectorFor_AsksForTheAnalysisConnectionsModelWide()
    {
        // The study is not related to its connections yet — relating it is what the read is for — so a
        // traversal from the site reaches none of them.
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        selector.MarkedTypes.Should().Equal(CoveringSourceResolver.SiteAnalysisConnectionFlag);
    }

    [Fact]
    public void SelectorFor_WalksPlacesBeforeCoverage()
    {
        // Traverse rules compose over the set built so far, so isIn must come before covers: asking
        // for the incoming coverage edges before the places exist finds nothing. Only that ordering is
        // pinned — a rule running from the seed set can sit anywhere, and pinning the whole list makes
        // adding one look like a regression.
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());
        var predicates = selector.Traverse!.Select(rule => rule.Predicate).ToList();

        predicates.IndexOf(CoveringSourceResolver.IsInPredicate)
            .Should().BeLessThan(predicates.IndexOf(CoveringSourceResolver.CoversPredicate));
        selector.Traverse!
            .Single(rule => rule.Predicate == CoveringSourceResolver.CoversPredicate)
            .Direction.Should().Be("incoming");
        selector.IncludeRelationships.Should().BeTrue();
    }

    [Fact]
    public void SelectorFor_AsksForEveryPredicateItCompares()
    {
        // A predicate the selector never names arrives as an unnamed Thing, and the walk then reads it
        // as "covers nothing" — a wrong answer wearing the shape of a valid one.
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        selector.Names.Should().Contain(new[]
        {
            CoveringSourceResolver.IsInPredicate,
            CoveringSourceResolver.CoversPredicate,
            CoveringSourceResolver.ResolvedByPredicate,
            CoveringSourceResolver.StudiesPredicate,
            CoveringSourceResolver.HasPredicate,
            CoveringSourceResolver.IsPredicateName,
        });
    }

    [Fact]
    public void Resolve_IgnoresAnEdgeWhosePredicateThingIsAbsent()
    {
        // Predicate Things arrive because the selector names them. One missing must read as "not this
        // predicate" rather than throwing on the path that serves a discovery run.
        var siteId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var snapshot = new SnapshotDocument(
            0,
            new List<SnapshotThing> { Thing(siteId, "WillowBend"), Thing(placeId, "Portugal") },
            new List<SnapshotRelationship> { Edge(siteId, Guid.NewGuid(), placeId) });

        var covering = CoveringSourceResolver.Resolve(snapshot, siteId);

        covering.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ThingsWithNoName_AreStillSelectedAndReportedBlank()
    {
        // A snapshot Thing's name is optional, and this runs on the path that serves a discovery
        // run — a missing name has to read as blank rather than end the run.
        var site = Guid.NewGuid();
        var place = Guid.NewGuid();
        var source = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var isIn = Guid.NewGuid();
        var covers = Guid.NewGuid();
        var resolvedBy = Guid.NewGuid();
        var snapshot = new SnapshotDocument(
            0,
            new List<SnapshotThing>
            {
                Thing(site, "WillowBend"),
                Thing(place, "Portugal"),
                Unnamed(source),
                Unnamed(endpoint),
                Thing(isIn, CoveringSourceResolver.IsInPredicate),
                Thing(covers, CoveringSourceResolver.CoversPredicate),
                Thing(resolvedBy, CoveringSourceResolver.ResolvedByPredicate),
            },
            new List<SnapshotRelationship>
            {
                Edge(site, isIn, place),
                Edge(source, covers, place),
                Edge(source, resolvedBy, endpoint),
            });

        var covering = CoveringSourceResolver.Resolve(snapshot, site);

        covering.Should().ContainSingle();
        covering[0].Name.Should().BeEmpty();
        covering[0].EndpointName.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_PredicateNameMatchIsCaseInsensitive()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", "IsIn", "Portugal")
            .Relate("OpenMeteo", "Covers", "Portugal")
            .Relate("OpenMeteo", "ResolvedBy", "OpenMeteoEndpoint");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Select(source => source.Name).Should().Equal("OpenMeteo");
    }
}
