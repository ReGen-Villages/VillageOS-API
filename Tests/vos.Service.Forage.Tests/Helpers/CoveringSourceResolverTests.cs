using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

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

        public ModelBuilder WithValue(string name, string property, string json)
        {
            var id = Id(name);
            var existing = _things.Single(thing => thing.Id == id);
            var properties = new Dictionary<string, SnapshotProperty>(existing.Properties)
            {
                [property] = new(JsonDocument.Parse(json).RootElement, null, null),
            };

            _things.RemoveAll(thing => thing.Id == id);
            _things.Add(new SnapshotThing(
                id, existing.Name, existing.IsArchetype, properties,
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
    public void Resolve_SourceDeclaringNothingItResolvesOnto_IsCalledOnceAboutTheSite()
    {
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");
        SourceCovering(model, "OpenMeteo", "Portugal");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls.Should().ContainSingle()
            .Which.SubjectId.Should().Be(model.Id("WillowBend"));
    }

    [Fact]
    public void Resolve_ACallCarriesTheSitesOwnValues()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .WithValue("WillowBend", "latitude", "39.4");
        SourceCovering(model, "OpenMeteo", "Portugal");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls[0].Values["latitude"].Should().Be("39.4");
    }

    [Fact]
    public void Resolve_APlacesValueAddressesTheCall()
    {
        // The portal's divisions are exactly what a Place is, so the division code a call needs sits
        // on the Place — and the coverage walk already returns it, so this costs no extra read.
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .WithValue("Portugal", "hazardPortalDivision", "\"2062\"");
        SourceCovering(model, "HazardPortal", "Portugal");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls[0].Values["hazardPortalDivision"].Should().Be("2062");
    }

    [Fact]
    public void Resolve_TheNearestPlacesValueWins()
    {
        // A division's value is about somewhere smaller than its country's. Taking the far one would
        // address every call at the widest place that happens to carry the name.
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Santarem")
            .Relate("Santarem", CoveringSourceResolver.IsInPredicate, "Portugal")
            .WithValue("Santarem", "hazardPortalDivision", "\"2062\"")
            .WithValue("Portugal", "hazardPortalDivision", "\"185\"");
        SourceCovering(model, "HazardPortal", "Portugal");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls[0].Values["hazardPortalDivision"].Should().Be("2062");
    }

    [Fact]
    public void Resolve_TheSitesOwnValueBeatsAPlaces()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .WithValue("WillowBend", "elevation", "\"120\"")
            .WithValue("Portugal", "elevation", "\"9\"");
        SourceCovering(model, "OpenMeteo", "Portugal");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls[0].Values["elevation"].Should().Be("120");
    }

    [Fact]
    public void Resolve_TwoPlacesAtTheSameDistanceDisagreeing_DropTheName()
    {
        // Relationship order is not defined, so taking either would address different calls on
        // different runs. A name the places agree on is still a value.
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Santarem")
            .WithValue("Portugal", "hazardPortalDivision", "\"185\"")
            .WithValue("Santarem", "hazardPortalDivision", "\"2062\"")
            .WithValue("Portugal", "region", "\"iberia\"")
            .WithValue("Santarem", "region", "\"iberia\"");
        SourceCovering(model, "OpenMeteo", "Portugal");

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"))[0].Calls[0].Values;

        values.Should().NotContainKey("hazardPortalDivision");
        values["region"].Should().Be("iberia");
    }

    // A source that resolves onto an archetype, and a site holding two Things of it — the shape the
    // hazard portal is registered in, where every route wants one assessment's codes.
    private static ModelBuilder ResolvingOnto(string source, string archetype, params string[] owned)
    {
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");
        SourceCovering(model, source, "Portugal");
        model.Relate(source, CoveringSourceResolver.ResolvesOntoPredicate, archetype);
        model.Archetype(archetype);
        foreach (var thing in owned)
        {
            model.Relate("WillowBend", CoveringSourceResolver.HasPredicate, thing)
                 .Relate(thing, CoveringSourceResolver.IsPredicateName, archetype);
        }

        return model;
    }

    [Fact]
    public void Resolve_SourceResolvingOntoAnArchetype_IsCalledOncePerThingTheSiteHasOfIt()
    {
        var model = ResolvingOnto("HazardPortal", "HazardAssessment", "wildfire assessment", "flood assessment");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls.Select(call => call.SubjectName)
            .Should().Equal("flood assessment", "wildfire assessment");
        covering[0].Calls.Select(call => call.SubjectId)
            .Should().Equal(model.Id("flood assessment"), model.Id("wildfire assessment"));
    }

    [Fact]
    public void Resolve_APerSubjectCallIsAddressedByWhatItsSubjectReaches()
    {
        // The portal's code for a hazard hangs off the type Thing the assessment assesses — a word on
        // the assessment could carry nothing — and the division code still arrives from the Place, so
        // one call holds both halves of the address.
        var model = ResolvingOnto("HazardPortal", "HazardAssessment", "flood assessment")
            .Relate("flood assessment", CoveringSourceResolver.AssessesPredicate, "river-flood")
            .WithValue("river-flood", "hazardPortalCode", "\"FL\"")
            .WithValue("Portugal", "hazardPortalDivision", "\"185\"");

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"))[0].Calls[0].Values;

        values["hazardPortalCode"].Should().Be("FL");
        values["hazardPortalDivision"].Should().Be("185");
    }

    [Fact]
    public void Resolve_ASubjectsOwnValueBeatsItsVocabularysAndTheSites()
    {
        var model = ResolvingOnto("HazardPortal", "HazardAssessment", "flood assessment")
            .Relate("flood assessment", CoveringSourceResolver.AssessesPredicate, "river-flood")
            .WithValue("flood assessment", "detail", "\"own\"")
            .WithValue("river-flood", "detail", "\"vocabulary\"")
            .WithValue("WillowBend", "detail", "\"site\"")
            .WithValue("river-flood", "hazardPortalCode", "\"FL\"")
            .WithValue("WillowBend", "hazardPortalCode", "\"site\"");

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"))[0].Calls[0].Values;

        values["detail"].Should().Be("own");
        values["hazardPortalCode"].Should().Be("FL", "what the subject reaches is nearer than the site");
    }

    [Fact]
    public void Resolve_AnArchetypesValuesDoNotAddressACall()
    {
        // A type's value is a default for a kind, the same reason inherited values never address a
        // call — so the is edge is not among the edges a subject's address is drawn along.
        var model = ResolvingOnto("HazardPortal", "HazardAssessment", "flood assessment")
            .WithValue("HazardAssessment", "hazardPortalCode", "\"XX\"");

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"))[0].Calls[0].Values;

        values.Should().NotContainKey("hazardPortalCode");
    }

    [Fact]
    public void Resolve_AThingTheSiteHasThatIsNotOfTheArchetype_IsNotCalledAbout()
    {
        var model = ResolvingOnto("HazardPortal", "HazardAssessment", "flood assessment");
        model.Relate("WillowBend", CoveringSourceResolver.HasPredicate, "a contact");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls.Select(call => call.SubjectName).Should().Equal("flood assessment");
    }

    [Fact]
    public void Resolve_AThingOfTheArchetypeTheSiteDoesNotHave_IsNotCalledAbout()
    {
        // Another site's assessment is of the same archetype. Calling about it would write a reading
        // onto a Thing this run was never dispatched for.
        var model = ResolvingOnto("HazardPortal", "HazardAssessment", "flood assessment");
        model.Relate("elsewhere assessment", CoveringSourceResolver.IsPredicateName, "HazardAssessment");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls.Select(call => call.SubjectName).Should().Equal("flood assessment");
    }

    [Fact]
    public void Resolve_SourceResolvingOntoThingsTheSiteHasNoneOf_KeepsTheSourceWithNoCalls()
    {
        // Nothing to fetch is not a failure — the caller reports it as nothing to resolve rather
        // than an outage, the same rule a site with no study is reported under.
        var model = ResolvingOnto("HazardPortal", "HazardAssessment");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Should().ContainSingle();
        covering[0].Calls.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ResolutionThroughAnIntermediateType_IsStillCalledAbout()
    {
        var model = ResolvingOnto("HazardPortal", "HazardAssessment")
            .Relate("WillowBend", CoveringSourceResolver.HasPredicate, "flood assessment")
            .Relate("flood assessment", CoveringSourceResolver.IsPredicateName, "ProjectAssessment")
            .Relate("ProjectAssessment", CoveringSourceResolver.IsPredicateName, "HazardAssessment");
        model.Archetype("ProjectAssessment");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering[0].Calls.Select(call => call.SubjectName).Should().Equal("flood assessment");
    }

    [Fact]
    public void Resolve_ResolvesOntoAThingThatIsNotAnArchetype_MakesNoCallAtAll()
    {
        // The declaration is there and unusable. A per-site call in its place would be refused for
        // its unfilled placeholders, and the run would report the provider for an outage it had no
        // part in.
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");
        SourceCovering(model, "HazardPortal", "Portugal");
        model.Relate("HazardPortal", CoveringSourceResolver.ResolvesOntoPredicate, "not a type");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"));

        covering.Should().ContainSingle();
        covering[0].Calls.Should().BeEmpty();
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
    public void AnalysisOf_SiteWithTwoStudies_IsRefusedRatherThanPickedBetween()
    {
        // Relationship order is not defined, so choosing would analyse a different study on different
        // runs and report neither choice.
        var model = StudyOf("WillowBend")
            .Relate("SecondStudy", CoveringSourceResolver.StudiesPredicate, "WillowBend");
        MarkedConnection(model, "balancesEnergy", "EnergyBalance prototype");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend")).Should().BeNull();
    }

    [Fact]
    public void AnalysisOf_TheSameStudyRelatedTwice_IsStillThatStudy()
    {
        // A duplicate edge is one study named twice, not two studies. Refusing it would take an analysis
        // away over a redundancy that changes no answer.
        var model = StudyOf("WillowBend")
            .Relate("WillowBendStudy", CoveringSourceResolver.StudiesPredicate, "WillowBend");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .StudyId.Should().Be(model.Id("WillowBendStudy"));
    }

    [Fact]
    public void AnalysisOf_IntermediateArchetypeBindingItsOwnService_IsNotATrigger()
    {
        // A type between a connection and the mark may bind a service of its own as a template. It is
        // still a type, and dispatching it would make the edge's predicate an archetype rather than a
        // connection — so only the member below it is a trigger.
        var model = StudyOf("WillowBend")
            .Relate("PlatformAnalysisConnection", CoveringSourceResolver.IsPredicateName, "SiteAnalysisConnection")
            .Relate("PlatformAnalysisConnection", CoveringSourceResolver.HasPredicate, "template service")
            .Relate("template service", CoveringSourceResolver.IsPredicateName, "EnergyBalance prototype")
            .Relate("balancesEnergy", CoveringSourceResolver.IsPredicateName, "PlatformAnalysisConnection")
            .Relate("balancesEnergy", CoveringSourceResolver.HasPredicate, "balancesEnergy service")
            .Relate("balancesEnergy service", CoveringSourceResolver.IsPredicateName, "EnergyBalance prototype");
        model.Archetype("PlatformAnalysisConnection");
        model.Archetype("SiteAnalysisConnection", CoveringSourceResolver.SiteAnalysisConnectionFlag);
        model.Archetype("EnergyBalance prototype");

        CoveringSourceResolver.AnalysisOf(model.Build(), model.Id("WillowBend"))!
            .Triggers.Select(trigger => trigger.ConnectionName).Should().Equal("balancesEnergy");
    }

    [Fact]
    public void AnalysisOf_IsEdgeFromAThingOutsideTheSnapshot_IsSkipped()
    {
        // Incident edges arrive for every Thing in the set, including ones whose other end was not
        // selected. Reading a connection that is not there would dispatch against a Thing this run
        // knows nothing about.
        var site = Guid.NewGuid();
        var study = Guid.NewGuid();
        var studies = Guid.NewGuid();
        var isEdge = Guid.NewGuid();
        var marked = Guid.NewGuid();
        var snapshot = new SnapshotDocument(
            0,
            new List<SnapshotThing>
            {
                Thing(site, "WillowBend"),
                Thing(study, "WillowBendStudy"),
                Thing(studies, CoveringSourceResolver.StudiesPredicate),
                Thing(isEdge, CoveringSourceResolver.IsPredicateName),
                new(marked, "SiteAnalysisConnection", true,
                    new Dictionary<string, SnapshotProperty>
                    {
                        [CoveringSourceResolver.SiteAnalysisConnectionFlag] =
                            new(JsonDocument.Parse("true").RootElement, null, null),
                    },
                    new Dictionary<string, InheritedPropertySet>(),
                    Array.Empty<string>(), Array.Empty<Guid>()),
            },
            new List<SnapshotRelationship>
            {
                Edge(study, studies, site),
                Edge(Guid.NewGuid(), isEdge, marked),
            });

        CoveringSourceResolver.AnalysisOf(snapshot, site)!.Triggers.Should().BeEmpty();
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
        // traversal from the site reaches none of them. The coverage archetype is asked for model-wide
        // for a different reason: a run mints against it, so it has to arrive before any coverage exists
        // and there is nothing to traverse from.
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        selector.MarkedTypes.Should().Equal(
            CoveringSourceResolver.SiteAnalysisConnectionFlag,
            CoveringSourceResolver.SourceCoverageArchetypeFlag);
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
            CoveringSourceResolver.ResolvesOntoPredicate,
            CoveringSourceResolver.AssessesPredicate,
            CoveringSourceResolver.StudiesPredicate,
            CoveringSourceResolver.HasPredicate,
            CoveringSourceResolver.IsPredicateName,
        });
    }

    [Fact]
    public void SelectorFor_WalksWhatHoldsASubjectBeforeWhatAddressesIt()
    {
        // Same composition rule as isIn before covers: what a source resolves onto joins the set
        // only once the sources are in it, and what an assessment assesses only once the site's
        // assessments are — a rule applied before its starting points exist finds nothing.
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());
        var predicates = selector.Traverse!.Select(rule => rule.Predicate).ToList();

        predicates.IndexOf(CoveringSourceResolver.CoversPredicate)
            .Should().BeLessThan(predicates.IndexOf(CoveringSourceResolver.ResolvesOntoPredicate));
        predicates.IndexOf(CoveringSourceResolver.HasPredicate)
            .Should().BeLessThan(predicates.IndexOf(CoveringSourceResolver.AssessesPredicate));
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
