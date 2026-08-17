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

        public SnapshotDocument Build() => new(0, _things, _edges);
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
    public void AnalysisPipelineOf_SiteAnalysedByAPipeline_IsThatPipeline()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.AnalysedByPredicate, "SiteAnalysis");

        var pipeline = CoveringSourceResolver.AnalysisPipelineOf(model.Build(), model.Id("WillowBend"));

        pipeline.Should().Be(model.Id("SiteAnalysis"));
    }

    [Fact]
    public void AnalysisPipelineOf_SiteAnalysedByNothing_IsNull()
    {
        // Not a failure: nothing was ever going to run, and reporting it as one would blame the run
        // for a gap in the model — the same rule a source with no registration is left out under.
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");

        CoveringSourceResolver.AnalysisPipelineOf(model.Build(), model.Id("WillowBend")).Should().BeNull();
    }

    [Fact]
    public void AnalysisPipelineOf_AnotherSitesPipeline_IsNotReturned()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("Elsewhere", CoveringSourceResolver.AnalysedByPredicate, "SiteAnalysis");

        CoveringSourceResolver.AnalysisPipelineOf(model.Build(), model.Id("WillowBend")).Should().BeNull();
    }

    [Fact]
    public void AnalysisPipelineOf_SnapshotHoldingAnUnnamedThing_StillFindsThePipeline()
    {
        // A snapshot Thing's name is optional, and this runs on the path that serves a discovery run:
        // one unnamed Thing anywhere in the snapshot must not stop the site's pipeline being found.
        var site = Guid.NewGuid();
        var pipeline = Guid.NewGuid();
        var analysedBy = Guid.NewGuid();
        var snapshot = new SnapshotDocument(
            0,
            new List<SnapshotThing>
            {
                Thing(site, "WillowBend"),
                Thing(pipeline, "SiteAnalysis"),
                Thing(analysedBy, CoveringSourceResolver.AnalysedByPredicate),
                Unnamed(Guid.NewGuid()),
            },
            new List<SnapshotRelationship> { Edge(site, analysedBy, pipeline) });

        CoveringSourceResolver.AnalysisPipelineOf(snapshot, site).Should().Be(pipeline);
    }

    [Fact]
    public void AnalysisPipelineOf_PredicateNameMatchIsCaseInsensitive()
    {
        var model = new ModelBuilder().Relate("WillowBend", "AnalysedBy", "SiteAnalysis");

        CoveringSourceResolver.AnalysisPipelineOf(model.Build(), model.Id("WillowBend"))
            .Should().Be(model.Id("SiteAnalysis"));
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
            CoveringSourceResolver.AnalysedByPredicate,
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
