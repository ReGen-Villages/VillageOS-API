using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

// What a run reads out of the model before it can work a site's administrative division out: the two
// registrations to call, what the search's address calls the name it takes, and the properties to write
// what it settled on into.
//
// Every one of them is read rather than named here, and half a declaration is no declaration: a run that
// made the first call and could not make the second would have asked a provider for nothing.
public class DivisionLookupTests
{
    private const string AreaNameAddress =
        "https://example.test/reverse?lat={latitude}&lon={longitude}&accept-language=en";

    private const string SearchAddress = "https://example.test/administrativedivision?q={areaName}";

    private static ModelBuilder WithBothLookups() => new ModelBuilder()
        .Stating("area-name-at-position", CoveringSourceResolver.AreaNameLookupFlag, true)
        .Stating("area-name-at-position", "url", AreaNameAddress)
        .Stating("hazard-division-search", CoveringSourceResolver.HazardDivisionLookupFlag, true)
        .Stating("hazard-division-search", "url", SearchAddress)
        .Stating("hazard-division-search", "divisionCodeProperty", "hazardPortalDivision")
        .Stating("hazard-division-search", "divisionNameProperty", "hazardPortalDivisionName");

    [Fact]
    public void BothRegistrationsAreFoundByTheMarkTheyCarry()
    {
        var lookup = CoveringSourceResolver.DivisionLookupIn(WithBothLookups().Build());

        lookup!.AreaNameEndpoint.Should().Be("area-name-at-position");
        lookup.SearchEndpoint.Should().Be("hazard-division-search");
    }

    // Read from the address rather than declared beside it, so the name a run fills and the placeholder it
    // fills cannot disagree.
    [Fact]
    public void TheAreaNameIsTakenFromTheSearchesOwnAddress()
    {
        CoveringSourceResolver.DivisionLookupIn(WithBothLookups().Build())!
            .SearchAreaNameParameter.Should().Be("areaName");
    }

    [Fact]
    public void TheSearchNamesThePropertiesARunWritesWhatItSettledOnInto()
    {
        var lookup = CoveringSourceResolver.DivisionLookupIn(WithBothLookups().Build())!;

        lookup.CodeProperty.Should().Be("hazardPortalDivision");
        lookup.NameProperty.Should().Be("hazardPortalDivisionName");
    }

    [Theory]
    [InlineData("area-name-at-position", CoveringSourceResolver.AreaNameLookupFlag)]
    [InlineData("hazard-division-search", CoveringSourceResolver.HazardDivisionLookupFlag)]
    [InlineData("hazard-division-search", "url")]
    [InlineData("hazard-division-search", "divisionCodeProperty")]
    [InlineData("hazard-division-search", "divisionNameProperty")]
    public void AModelMissingAnyHalfOfTheDeclarationOffersNoLookup(string registration, string property)
    {
        var model = WithBothLookups().Without(registration, property).Build();

        CoveringSourceResolver.DivisionLookupIn(model).Should().BeNull();
    }

    [Fact]
    public void AModelDeclaringNeitherLookupOffersNone()
    {
        CoveringSourceResolver.DivisionLookupIn(new ModelBuilder().Build()).Should().BeNull();
    }

    // One name is all a run has to fill, and a call left with an unfilled placeholder is refused before the
    // provider is contacted — so a second placeholder is a declaration this cannot honour rather than one
    // to guess at.
    [Theory]
    [InlineData("https://example.test/administrativedivision")]
    [InlineData("https://example.test/administrativedivision?q={areaName}&in={country}")]
    public void ASearchAddressTakingAnythingButOneNameOffersNoLookup(string address)
    {
        var model = WithBothLookups().Stating("hazard-division-search", "url", address).Build();

        CoveringSourceResolver.DivisionLookupIn(model).Should().BeNull();
    }

    // No edge reaches either: neither covers a Place, so a coverage walk finds nothing to traverse to them.
    [Fact]
    public void TheSitesReadAsksForBothMarksModelWide()
    {
        var selector = CoveringSourceResolver.SelectorFor(Guid.NewGuid());

        selector.MarkedArchetypes.Should().Contain(CoveringSourceResolver.AreaNameLookupFlag);
        selector.MarkedArchetypes.Should().Contain(CoveringSourceResolver.HazardDivisionLookupFlag);
    }

    // The address a run asks both for the site's coordinates and for whether anything already supplies a
    // division code, so a project that coded its own Place keeps a run from resolving over it.
    [Fact]
    public void APlacesDivisionCodeReachesTheSitesAddress()
    {
        var builder = new ModelBuilder()
            .Relate("WillowBend", "isIn", "Portugal")
            .Stating("WillowBend", "latitude", "39.5")
            .Stating("Portugal", "latitude", "39.9")
            .Stating("Portugal", "hazardPortalDivision", "2409");

        var address = CoveringSourceResolver.AddressOf(builder.Build(), builder.Id("WillowBend"));

        address["hazardPortalDivision"].Should().Be("2409");
        address["latitude"].Should().Be("39.5", "the site's own value decides a name a Place also holds");
    }

    // Under, because a value the call's subject states is about that subject and this one is about the site.
    [Fact]
    public void AWorkedOutValueAddressesEveryCallWithoutBeatingASubjectsOwn()
    {
        var subject = Guid.NewGuid();
        var covering = new[]
        {
            new CoveringSource(Guid.NewGuid(), "ThinkHazard", "hazard-grading",
            [
                new SourceCall(subject, "River flood", new Dictionary<string, string>
                {
                    ["hazardPortalCode"] = "FL",
                    ["hazardPortalDivision"] = "already stated",
                }),
            ]),
        };

        var addressed = CoveringSourceResolver.AlsoAddressedWith(covering, new Dictionary<string, string>
        {
            ["hazardPortalDivision"] = "24889",
            ["hazardPortalDivisionName"] = "Portugal / Santarem / Santarem",
        });

        var values = addressed.Single().Calls.Single().Values;
        values["hazardPortalCode"].Should().Be("FL");
        values["hazardPortalDivision"].Should().Be("already stated");
        values["hazardPortalDivisionName"].Should().Be("Portugal / Santarem / Santarem");
    }
}
