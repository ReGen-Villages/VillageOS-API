using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared;
using vos.Service.Forage.Services;
using vos.Service.Forage.Steps;
using vos.Service.Shared.Roles;
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

    // One place to build a model, so a test reads as the shape it is asserting rather than as setup.
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

        // The role Thing, under the identifier every model declares it by, binding the named Thing along `binds`.
        public ModelBuilder Bound(Role role, string name)
        {
            if (_things.All(thing => thing.Id != role.Id)) _things.Add(Thing(role.Id, role.Word));
            _edges.Add(Edge(role.Id, Id(RoleBindings.BindsPredicateName), Id(name)));
            return this;
        }

        public ModelBuilder Archetype(string name)
        {
            var id = Id(name);
            var properties = new Dictionary<string, SnapshotProperty>();

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

    // A source is callable only through its registration, so every scenario below wires both.
    private static ModelBuilder SourceCovering(ModelBuilder model, string source, string place) =>
        model.Relate(source, CoveringSourceResolver.CoversPredicate, place)
             .Relate(source, CoveringSourceResolver.ResolvedByPredicate, source + "Endpoint");

    [Fact]
    public void Resolve_ASourceCoveringAlongAPredicateNamedInAnotherCase_IsNotSelected()
    {
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");
        model.Relate("NationalFloodPortal", CoveringSourceResolver.CoversPredicate.ToUpperInvariant(), "Portugal")
             .Relate("NationalFloodPortal", CoveringSourceResolver.ResolvedByPredicate, "NationalFloodPortalEndpoint");

        CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"))
            .Should().BeEmpty("Thing names are matched exactly, case included");
    }

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
    public void Resolve_ACallCarriesWhatTheSiteStates()
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

    // A source's address is filled from the subject outward, then from the source itself: its own values
    // by name, the window's two dates from how far back it reaches, and the provider names of the
    // variables it provides, joined — the shape the climate history is registered in, where a deployment
    // tunes all three by editing the model and the call follows.
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static ModelBuilder ClimateHistory(ModelBuilder model)
    {
        SourceCovering(model, "ClimateHistory", "Portugal");
        model.WithValue("ClimateHistory", "historyYears", "10")
             .WithValue("ClimateHistory", "granularity", "\"hourly\"")
             .Bound(CoveringSourceResolver.ProvidedVariableRole, "provides")
             .Relate("ClimateHistory", "provides", "temperatureCelsius")
             .Relate("ClimateHistory", "provides", "rainMillimetres")
             .WithValue("temperatureCelsius", CoveringSourceResolver.ProviderNameProperty, "\"temperature_2m\"")
             .WithValue("rainMillimetres", CoveringSourceResolver.ProviderNameProperty, "\"rain\"");
        return model;
    }

    [Fact]
    public void Resolve_ASliceLengthStatedAsANumber_ReachesThePlanAsItsDigitsAndGivesOneCall()
    {
        var model = ClimateHistory(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"))
            .WithValue("ClimateHistory", PlannedCalls.DaysPerSliceProperty, "13000");

        var covering = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"), Today)[0];
        var planned = PlannedCalls.From([new OutstandingCall(covering, covering.Calls[0], Guid.NewGuid(), Attempts: 0)], daysPerSlice: 365);

        covering.Calls[0].Values[PlannedCalls.DaysPerSliceProperty].Should().Be("13000");
        planned.Should().ContainSingle().Which.Slice.Should().BeNull();
    }

    [Fact]
    public void Resolve_AThingReachedThroughAnotherPredicate_IsNotAmongTheVariables()
    {
        var model = ClimateHistory(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"))
            .Relate("ClimateHistory", "documents", "humidityPercent")
            .WithValue("humidityPercent", CoveringSourceResolver.ProviderNameProperty, "\"relative_humidity_2m\"");

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"), Today)[0].Calls[0].Values;

        values[CoveringSourceResolver.VariablesPlaceholder].Should().Be("rain,temperature_2m");
    }

    [Fact]
    public void Resolve_ASourceDeclaringItsReachAndProvidingVariables_IsCalledWithTheWindowTheGranularityAndTheNamesJoined()
    {
        var model = ClimateHistory(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .WithValue("WillowBend", "latitude", "39.4"));

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"), Today)[0].Calls[0].Values;

        values["latitude"].Should().Be("39.4");
        values["granularity"].Should().Be("hourly");
        values["historyYears"].Should().Be("10");
        values["startDate"].Should().Be("2016-09-18");
        values["endDate"].Should().Be("2026-09-18");
        values["variables"].Should().Be("rain,temperature_2m", "the provider names are joined in the Things' name order, so two runs address one call the same way");
    }

    [Fact]
    public void Resolve_ANameTheSubjectAndTheSourceBothCarry_TakesTheSubjects()
    {
        var model = ClimateHistory(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .WithValue("WillowBend", "granularity", "\"daily\""));

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"), Today)[0].Calls[0].Values;

        values["granularity"].Should().Be("daily");
    }

    // The window's dates go to a provider's archive, which reads the Gregorian calendar; a host counting
    // in another calendar would otherwise address the same window as a different decade.
    [Fact]
    public void Resolve_AWindowIsWrittenInTheGregorianCalendarWhateverTheHostsCulture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
        try
        {
            var model = ClimateHistory(new ModelBuilder()
                .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"));

            var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"), Today)[0].Calls[0].Values;

            values["startDate"].Should().Be("2016-09-18");
            values["endDate"].Should().Be("2026-09-18");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Resolve_AReachThatIsNotAWholeNumberOfYears_GivesNoWindow()
    {
        var model = ClimateHistory(new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal"))
            .WithValue("ClimateHistory", "historyYears", "\"ten\"");

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"), Today)[0].Calls[0].Values;

        values.Should().NotContainKey("startDate").And.NotContainKey("endDate");
        values["historyYears"].Should().Be("ten", "the value still fills its own placeholder; only the window is not worked out from it");
    }

    [Fact]
    public void Resolve_ASourceDeclaringNoReachAndProvidingNothing_IsAddressedAsBefore()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .WithValue("WillowBend", "latitude", "39.4");
        SourceCovering(model, "OpenMeteo", "Portugal");

        var values = CoveringSourceResolver.Resolve(model.Build(), model.Id("WillowBend"), Today)[0].Calls[0].Values;

        values.Keys.Should().BeEquivalentTo("latitude");
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
                 .Relate(thing, PlatformPredicates.Is, archetype);
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
        // call — so the is relationship is not among the relationships a subject's address is drawn along.
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
        // onto a Thing this run was never called for.
        var model = ResolvingOnto("HazardPortal", "HazardAssessment", "flood assessment");
        model.Relate("elsewhere assessment", PlatformPredicates.Is, "HazardAssessment");

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
            .Relate("flood assessment", PlatformPredicates.Is, "ProjectAssessment")
            .Relate("ProjectAssessment", PlatformPredicates.Is, "HazardAssessment");
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
    public void StudiesOf_TheStudyThatStudiesTheSite_IsFoundWithItsName()
    {
        var model = StudyOf("WillowBend");

        CoveringSourceResolver.StudiesOf(model.Build(), model.Id("WillowBend"))
            .Should().Equal(new Study(model.Id("WillowBendStudy"), "WillowBendStudy"));
    }

    [Fact]
    public void StudiesOf_ASiteWithNoStudy_FindsNone()
    {
        var model = new ModelBuilder().Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal");

        CoveringSourceResolver.StudiesOf(model.Build(), model.Id("WillowBend")).Should().BeEmpty();
    }

    [Fact]
    public void StudiesOf_AnotherSitesStudy_IsNotFound()
    {
        var model = new ModelBuilder()
            .Relate("WillowBend", CoveringSourceResolver.IsInPredicate, "Portugal")
            .Relate("ElsewhereStudy", CoveringSourceResolver.StudiesPredicate, "Elsewhere");

        CoveringSourceResolver.StudiesOf(model.Build(), model.Id("WillowBend")).Should().BeEmpty();
    }

    [Fact]
    public void StudiesOf_TwoStudies_AreBothFoundInIdentifierOrder()
    {
        // Relationship order is not defined, so the order is the identifiers', and the caller refuses the pair.
        var model = StudyOf("WillowBend")
            .Relate("SecondStudy", CoveringSourceResolver.StudiesPredicate, "WillowBend");

        CoveringSourceResolver.StudiesOf(model.Build(), model.Id("WillowBend")).Select(study => study.Id)
            .Should().Equal(new[] { model.Id("WillowBendStudy"), model.Id("SecondStudy") }.Order());
    }

    [Fact]
    public void StudiesOf_TheSameStudyRelatedTwice_IsOneStudy()
    {
        // A duplicate relationship is one study named twice, not two studies, so it leaves the site discoverable.
        var model = StudyOf("WillowBend")
            .Relate("WillowBendStudy", CoveringSourceResolver.StudiesPredicate, "WillowBend");

        CoveringSourceResolver.StudiesOf(model.Build(), model.Id("WillowBend")).Select(study => study.Id)
            .Should().Equal(model.Id("WillowBendStudy"));
    }

    [Fact]
    public void StudiesOf_SnapshotHoldingAnUnnamedThing_StillFindsTheStudy()
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

        CoveringSourceResolver.StudiesOf(snapshot, site).Select(found => found.Id).Should().Equal(study);
    }

    [Fact]
    public void StudiesOf_AStudyAlongAPredicateNamedInAnotherCase_IsNotTheSitesStudy()
    {
        var model = new ModelBuilder().Relate("WillowBendStudy", "Studies", "WillowBend");

        CoveringSourceResolver.StudiesOf(model.Build(), model.Id("WillowBend"))
            .Should().BeEmpty("Thing names are matched exactly, case included");
    }

    [Fact]
    public void SelectorFor_AsksForNoConnectionModelWide()
    {
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        selector.BoundTypes.Should().Equal(CoveringSourceResolver.PlaceArchetypeRole.Id);
    }

    // A run mints against the coverage archetype, so it has to arrive before any coverage exists to
    // traverse from — but alone. Asked for with its members, every coverage in the model would arrive on
    // every site's read, one Thing per site and source already recorded anywhere; the coverages
    // about this site already come through the `appliesTo` traversal from the site and its assessments.
    [Fact]
    public void SelectorFor_AsksForTheCoverageArchetypeAlone()
    {
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        selector.BoundArchetypes.Should().Contain(CoveringSourceResolver.SourceCoverageRole.Id);
        selector.BoundTypes.Should().NotContain(CoveringSourceResolver.SourceCoverageRole.Id);
    }

    // A bound Thing arrives without the role Thing that binds it, so the read asks for every role by identifier
    // and follows the bindings, or it could not say which bound Thing plays which part.
    [Fact]
    public void SelectorFor_AsksForEveryRoleItReadsThrough()
    {
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        selector.Ids.Should().Contain(
        [
            CoveringSourceResolver.SiteRole.Id, CoveringSourceResolver.PlaceNestingRole.Id,
            CoveringSourceResolver.PlaceArchetypeRole.Id,
            CoveringSourceResolver.SourceCoverageRole.Id, CoveringSourceResolver.AreaNameLookupRole.Id,
            CoveringSourceResolver.LocalAreaNameLookupRole.Id, CoveringSourceResolver.HazardDivisionLookupRole.Id,
            CoveringSourceResolver.ProvidedVariableRole.Id,
        ]);
        selector.Names.Should().Contain(RoleBindings.BindsPredicateName);
        selector.Traverse.Should().Contain(RoleBindings.FollowBindings);
    }

    [Fact]
    public void SelectorFor_WalksPlacesBeforeCoverage()
    {
        // Traverse rules compose over the set built so far, so isIn must come before covers: asking
        // for the incoming coverage relationships before the places exist finds nothing. Only that ordering is
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
            PlatformPredicates.Is,
        });
    }

    [Fact]
    public void SelectorFor_ReachesTheVariablesASourceProvidesThroughTheBoundPredicateAndAsksForThePredicateAlone()
    {
        // The variables are composed from the relationships a source holds through the bound predicate, read out
        // of the snapshot, so both have to arrive: the variables by a traversal over the role rather than
        // a name the model chose, and the predicate through its role, since a traversal never brings the Thing
        // it was followed through. Without either, every live read would leave the placeholder unfilled.
        var role = CoveringSourceResolver.ProvidedVariableRole;
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());
        var roles = selector.Traverse!.Select(rule => rule.PredicateRole).ToList();
        var predicates = selector.Traverse!.Select(rule => rule.Predicate).ToList();

        roles.IndexOf(role.Id)
            .Should().BeGreaterThan(predicates.IndexOf(CoveringSourceResolver.CoversPredicate),
                "the sources join the set through covers, and a rule applied before its starting points exist finds nothing");
        selector.Traverse!.Single(rule => rule.PredicateRole == role.Id)
            .Predicate.Should().BeEmpty("the broker refuses a rule naming a predicate both ways");
        selector.BoundArchetypes.Should().Contain(role.Id);
        selector.BoundTypes.Should().NotContain(role.Id);
        selector.Ids.Should().Contain(role.Id, "the role Thing and its binding are what say which predicate it is");
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
    public void SelectorFor_CarriesEveryRelationshipASelectedThingHoldsOutgoingAndNoneWrittenIntoIt()
    {
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        var rule = selector.Relationships.Should().ContainSingle().Subject;
        rule.Direction.Should().Be("outgoing");
        rule.Predicate.Should().BeEmpty();
        rule.PredicateRole.Should().BeNull();
        selector.IncludeRelationships.Should().BeTrue("the broker refuses rules beside no relationships");
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
}
