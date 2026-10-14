using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using vos.Service.Forage.Helpers;
using vos.Service.Forage.Services;
using Xunit;

namespace vos.Service.Forage.Tests.Services;

// What a run does with the two division calls: when it makes them, what it writes, and what it leaves
// alone. Both providers are fakes here, so a test reads as the decision rather than as a network.
//
// The failure every case guards is a site whose hazard calls are all refused for an unfilled placeholder,
// which reaches a planner as an empty hazards table and reads as "no hazards here".
public class DivisionResolverTests
{
    private const string CodeProperty = "hazardPortalDivision";
    private const string NameProperty = "hazardPortalDivisionName";
    private const string AreaNameEndpoint = "area-name-at-position";
    private const string SearchEndpoint = "hazard-division-search";
    private const string AreaNameParameter = "areaName";

    private static readonly DivisionLookup Lookup = new(
        AreaNameEndpoint, SearchEndpoint, AreaNameParameter, CodeProperty, NameProperty);

    private const string InPortugal = """
        {"address": {"county": "Santarem", "country": "Portugal"}}
        """;

    private const string OnMarthasVineyard = """
        {"address": {"county": "Dukes County", "state": "Massachusetts", "country": "United States"}}
        """;

    private const string DivisionsNamedSantarem = """
        {"data": [
          {"code": 2409, "admin0": "Portugal", "admin1": "Santarem"},
          {"code": 8836, "admin0": "Brazil", "admin1": "Para", "admin2": "Santarem"},
          {"code": 24889, "admin0": "Portugal", "admin1": "Santarem", "admin2": "Santarem"}]}
        """;

    private const string DivisionsNamedMassachusetts = """
        {"data": [{"code": 3235, "admin0": "United States of America", "admin1": "Massachusetts"}]}
        """;

    private const string NoDivisionOfThatName = """{"data": []}""";

    private const string InPoland = """
        {"address": {"county": "Warsaw West County", "state": "Masovian Voivodeship",
                     "ISO3166-2-lvl4": "PL-14", "country": "Poland", "country_code": "pl"}}
        """;

    private const string DivisionsNamedMazowieckie = """
        {"data": [{"code": 2376, "admin0": "Poland", "admin1": "Mazowieckie"}]}
        """;

    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid NestingPredicateId = Guid.NewGuid();
    private static readonly Guid PolandId = Guid.NewGuid();
    private static readonly Guid MazowieckieId = Guid.NewGuid();

    private static readonly DivisionLookup LookupWithCodedPlaces = Lookup with { PlaceCodeProperty = "iso3166Code" };

    private static SiteCoverage CoverageOf(
        DivisionLookup? lookup, params (string Name, string Value)[] address) =>
        new(
            [
                new CoveringSource(Guid.NewGuid(), "ThinkHazard", "hazard-grading",
                [
                    new SourceCall(Guid.NewGuid(), "River flood",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["hazardPortalCode"] = "FL",
                        }),
                ]),
            ],
            null, [], null, lookup,
            address.ToDictionary(stated => stated.Name, stated => stated.Value, StringComparer.OrdinalIgnoreCase));

    private static SiteCoverage AtWillowBend() =>
        CoverageOf(Lookup, ("latitude", "39.5012"), ("longitude", "-8.4137"));

    private static SiteCoverage AtWillowBendWithNoLookupDeclared() =>
        CoverageOf(null, ("latitude", "39.5012"), ("longitude", "-8.4137"));

    private static CodedPlace Poland(string? division = null, bool siteIsIn = false) =>
        new(PolandId, "Poland", "PL", siteIsIn, ValuesOf(division));

    private static CodedPlace Mazowieckie(string? division = "2376", bool siteIsIn = false) =>
        new(MazowieckieId, "Mazowieckie", "PL-14", siteIsIn, ValuesOf(division));

    private static Dictionary<string, string> ValuesOf(string? division) =>
        division is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CodeProperty] = division };

    private static SiteCoverage AtKirsztajnowAmong(params CodedPlace[] places) =>
        CoverageOf(LookupWithCodedPlaces, ("latitude", "52.2833"), ("longitude", "20.3439"))
            with { CodedPlaces = new CodedPlaces(NestingPredicateId, places) };

    private static DivisionResolver ResolverOver(ScriptedReader reader, RecordingWriter writer) =>
        new(reader, writer, NullLogger<DivisionResolver>.Instance);

    private static string DivisionAddressing(SiteCoverage coverage) =>
        coverage.Covering.Single().Calls.Single().Values.GetValueOrDefault(CodeProperty, string.Empty);

    private sealed class ScriptedReader : IEndpointBodyReader
    {
        private readonly Queue<string?> _searchAnswers;
        private readonly string? _position;

        public readonly List<(string Endpoint, string? AreaName)> Asked = new();

        public ScriptedReader(string? position, params string?[] searchAnswers)
        {
            _position = position;
            _searchAnswers = new Queue<string?>(searchAnswers);
        }

        public Task<string?> ReadAsync(
            string endpointName,
            IReadOnlyDictionary<string, string> addressParameters,
            CancellationToken cancellationToken)
        {
            Asked.Add((endpointName, addressParameters.GetValueOrDefault(AreaNameParameter)));
            return Task.FromResult(endpointName == AreaNameEndpoint
                ? _position
                : _searchAnswers.Count > 0 ? _searchAnswers.Dequeue() : null);
        }
    }

    private sealed class RecordingWriter : ICoverageWriter
    {
        public readonly List<(Guid Thing, string Property, object? Value)> Facts = new();
        public readonly List<(Guid Subject, Guid Predicate, Guid Target)> Related = new();
        public string? RefusedProperty { get; init; }
        public bool RefusesRelating { get; init; }

        public Task<Guid?> MintAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<bool> RelateAsync(
            Guid subjectId, Guid predicateId, Guid targetId, CancellationToken cancellationToken)
        {
            if (RefusesRelating) return Task.FromResult(false);
            Related.Add((subjectId, predicateId, targetId));
            return Task.FromResult(true);
        }

        public Task<bool> WriteFactAsync(
            Guid thingId, string property, object? value, CancellationToken cancellationToken)
        {
            if (property == RefusedProperty) return Task.FromResult(false);
            Facts.Add((thingId, property, value));
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task TheFinestDivisionOfTheSitesCountryIsWrittenOntoIt()
    {
        var writer = new RecordingWriter();
        var resolver = ResolverOver(new ScriptedReader(InPortugal, DivisionsNamedSantarem), writer);

        await resolver.AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        writer.Facts.Should().Contain((SiteId, CodeProperty, "24889"));
        writer.Facts.Should().Contain((SiteId, NameProperty, "Portugal / Santarem / Santarem"));
    }

    // On this run, not the next: a site graded only after a whole dispatch had been waited out would sit
    // ungraded for as long as the platform allows a run to take.
    [Fact]
    public async Task TheResolvedDivisionAddressesThisRunsOwnCalls()
    {
        var resolver = ResolverOver(
            new ScriptedReader(InPortugal, DivisionsNamedSantarem), new RecordingWriter());

        var addressed = await resolver.AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        DivisionAddressing(addressed).Should().Be("24889");
    }

    // Found by the standard's code rather than by a name: the geocoder answers English, the portal holds
    // the division's own language, and neither matches a Polish site, while `PL-14` is the same on every
    // side. The Place's own division then addresses the gradings, so the portal is not searched at all.
    [Fact]
    public async Task ASiteIsRelatedToThePlaceCarryingTheCodeTheGeocoderAnsweredAndGradedAtItsDivision()
    {
        var reader = new ScriptedReader(InPoland, DivisionsNamedMazowieckie);
        var writer = new RecordingWriter();

        var addressed = await ResolverOver(reader, writer)
            .AddressAsync(SiteId, AtKirsztajnowAmong(Poland(), Mazowieckie()), CancellationToken.None);

        writer.Related.Should().Equal((SiteId, NestingPredicateId, MazowieckieId));
        DivisionAddressing(addressed).Should().Be("2376");
        reader.Asked.Should().ContainSingle().Which.Endpoint.Should().Be(AreaNameEndpoint);
        writer.Facts.Should().BeEmpty();
    }

    // The region's Place says more about the site than its country's, whichever the model lists first.
    [Fact]
    public async Task TheFinestCodedPlaceWins()
    {
        var writer = new RecordingWriter();

        var addressed = await ResolverOver(new ScriptedReader(InPoland), writer)
            .AddressAsync(SiteId, AtKirsztajnowAmong(Mazowieckie(), Poland("185")), CancellationToken.None);

        writer.Related.Should().Equal((SiteId, NestingPredicateId, MazowieckieId));
        DivisionAddressing(addressed).Should().Be("2376");
    }

    // The relationship is worth writing on its own: the next run reads the Place's values through it.
    [Fact]
    public async Task APlaceCarryingTheCodeButNoDivisionIsRelatedAndThePortalIsStillSearched()
    {
        var reader = new ScriptedReader(InPoland, NoDivisionOfThatName, DivisionsNamedMazowieckie);
        var writer = new RecordingWriter();

        var addressed = await ResolverOver(reader, writer)
            .AddressAsync(SiteId, AtKirsztajnowAmong(Mazowieckie(division: null)), CancellationToken.None);

        writer.Related.Should().Equal((SiteId, NestingPredicateId, MazowieckieId));
        reader.Asked.Select(asked => asked.AreaName).Should().Equal(null, "Warsaw West County", "Masovian Voivodeship");
        DivisionAddressing(addressed).Should().Be("2376");
        writer.Facts.Should().Contain((SiteId, CodeProperty, "2376"));
    }

    [Fact]
    public async Task ASiteAlreadyStandingInTheCodedPlaceIsNotRelatedAgain()
    {
        var writer = new RecordingWriter();

        await ResolverOver(new ScriptedReader(InPoland, DivisionsNamedMazowieckie), writer)
            .AddressAsync(SiteId, AtKirsztajnowAmong(Mazowieckie(division: null, siteIsIn: true)), CancellationToken.None);

        writer.Related.Should().BeEmpty();
    }

    // A refused relationship is the model's to explain; the search still grades the site on this run.
    [Fact]
    public async Task ARelationshipTheModelRefusedLeavesTheSearchToResolveTheDivision()
    {
        var writer = new RecordingWriter { RefusesRelating = true };

        var addressed = await ResolverOver(new ScriptedReader(InPoland, DivisionsNamedMazowieckie), writer)
            .AddressAsync(SiteId, AtKirsztajnowAmong(Mazowieckie()), CancellationToken.None);

        writer.Related.Should().BeEmpty();
        DivisionAddressing(addressed).Should().Be("2376");
        writer.Facts.Should().Contain((SiteId, CodeProperty, "2376"));
    }

    // Two Places claiming one code cannot both be where the site is, and relating it to either would be a guess.
    [Fact]
    public async Task TwoPlacesCarryingTheSameCodeAreNeitherTaken()
    {
        var writer = new RecordingWriter();
        var twice = new CodedPlace(Guid.NewGuid(), "Masovia", "PL-14", false, ValuesOf("99"));

        var addressed = await ResolverOver(new ScriptedReader(InPoland, DivisionsNamedMazowieckie), writer)
            .AddressAsync(SiteId, AtKirsztajnowAmong(Mazowieckie(), twice, Poland("185")), CancellationToken.None);

        writer.Related.Should().Equal((SiteId, NestingPredicateId, PolandId));
        DivisionAddressing(addressed).Should().Be("185");
    }

    [Fact]
    public async Task NoPlaceCarryingAnAnsweredCodeLeavesTheSearchAsItWas()
    {
        var writer = new RecordingWriter();

        var addressed = await ResolverOver(new ScriptedReader(InPoland, NoDivisionOfThatName, DivisionsNamedMazowieckie), writer)
            .AddressAsync(SiteId, AtKirsztajnowAmong(), CancellationToken.None);

        writer.Related.Should().BeEmpty();
        DivisionAddressing(addressed).Should().Be("2376");
    }

    // A project that coded its own Place decides, and a site that needs no lookup costs two providers
    // nothing on every run.
    [Fact]
    public async Task ASiteWhoseModelAlreadySuppliesADivisionIsLeftAlone()
    {
        var reader = new ScriptedReader(InPortugal, DivisionsNamedSantarem);
        var writer = new RecordingWriter();
        var coverage = CoverageOf(Lookup, ("latitude", "39.5012"), (CodeProperty, "2409"));

        await ResolverOver(reader, writer).AddressAsync(SiteId, coverage, CancellationToken.None);

        reader.Asked.Should().BeEmpty();
        writer.Facts.Should().BeEmpty();
    }

    // The geocoder answers "Dukes County" where the portal holds "Dukes", so the county finds nothing and
    // the state it also gave is a division the portal does hold.
    [Fact]
    public async Task AnAreaNameThePortalHoldsNoDivisionForFallsBackToTheCoarserName()
    {
        var reader = new ScriptedReader(OnMarthasVineyard, NoDivisionOfThatName, DivisionsNamedMassachusetts);
        var writer = new RecordingWriter();

        await ResolverOver(reader, writer).AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        reader.Asked.Should().Equal(
            (AreaNameEndpoint, null),
            (SearchEndpoint, "Dukes County"),
            (SearchEndpoint, "Massachusetts"));
        writer.Facts.Should().Contain((SiteId, CodeProperty, "3235"));
    }

    // A search that was refused is not a name the portal holds nothing for, and asking a coarser question
    // would turn one provider's outage into a coarser grading nobody chose.
    [Fact]
    public async Task ARefusedSearchStopsTheRunRatherThanAskingACoarserName()
    {
        var reader = new ScriptedReader(OnMarthasVineyard, null, DivisionsNamedMassachusetts);
        var writer = new RecordingWriter();

        await ResolverOver(reader, writer).AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        reader.Asked.Should().HaveCount(2);
        writer.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task ASiteWhoseCountryHoldsNoDivisionOfTheNameKeepsNoCode()
    {
        var writer = new RecordingWriter();
        var reader = new ScriptedReader(InPortugal, """{"data": [{"code": 8836, "admin0": "Brazil", "admin1": "Para", "admin2": "Santarem"}]}""");

        var addressed = await ResolverOver(reader, writer).AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        writer.Facts.Should().BeEmpty();
        DivisionAddressing(addressed).Should().BeEmpty();
    }

    // The provider was not reached, so nothing is known about where the site is — searching the portal for
    // a name nobody gave would ask about nowhere.
    [Fact]
    public async Task ARefusedAreaNameLookupSearchesForNothing()
    {
        var reader = new ScriptedReader(null, DivisionsNamedSantarem);
        var writer = new RecordingWriter();

        await ResolverOver(reader, writer).AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        reader.Asked.Should().ContainSingle().Which.Endpoint.Should().Be(AreaNameEndpoint);
        writer.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task AGeocoderThatNamedNoAreaLeavesTheDivisionUnresolved()
    {
        var reader = new ScriptedReader("""{"error": "Unable to geocode"}""", DivisionsNamedSantarem);
        var writer = new RecordingWriter();

        await ResolverOver(reader, writer).AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        reader.Asked.Should().ContainSingle();
        writer.Facts.Should().BeEmpty();
    }

    [Fact]
    public async Task AModelDeclaringNoLookupIsAddressedWithWhateverItsPlacesCarry()
    {
        var reader = new ScriptedReader(InPortugal, DivisionsNamedSantarem);

        var addressed = await ResolverOver(reader, new RecordingWriter())
            .AddressAsync(SiteId, AtWillowBendWithNoLookupDeclared(), CancellationToken.None);

        reader.Asked.Should().BeEmpty();
        DivisionAddressing(addressed).Should().BeEmpty();
    }

    // Addressed anyway, the gradings would resolve and be stamped against a division the model never
    // recorded, and nothing would ever resolve one again.
    [Fact]
    public async Task ACodeTheModelRefusedAddressesNothing()
    {
        var writer = new RecordingWriter { RefusedProperty = CodeProperty };

        var addressed = await ResolverOver(new ScriptedReader(InPortugal, DivisionsNamedSantarem), writer)
            .AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        writer.Facts.Should().BeEmpty();
        DivisionAddressing(addressed).Should().BeEmpty();
    }

    // The code is what addresses a grading, so a name that would not go leaves the gradings resolvable
    // rather than holding the whole site back for a label.
    [Fact]
    public async Task ANameTheModelRefusedStillLeavesTheGradingsAddressed()
    {
        var writer = new RecordingWriter { RefusedProperty = NameProperty };

        var addressed = await ResolverOver(new ScriptedReader(InPortugal, DivisionsNamedSantarem), writer)
            .AddressAsync(SiteId, AtWillowBend(), CancellationToken.None);

        writer.Facts.Should().Contain((SiteId, CodeProperty, "24889"));
        DivisionAddressing(addressed).Should().Be("24889");
    }
}
