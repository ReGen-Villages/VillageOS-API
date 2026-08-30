using FluentAssertions;
using vos.Service.Forage.Helpers;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

// The two provider replies a run works a site's administrative division out from, and the choosing
// between them. Every body here is shaped as the live providers answered it.
//
// What each guards is a wrong division reading exactly like a right one: `Santarem` names one division in
// Portugal and two in Brazil, and the Portuguese name exists both as a region graded high river flood and
// as the district inside it graded medium. A guess tells a landowner their land does not flood.
public class HazardDivisionTests
{
    private const string InPortugal = """
        {"place_id": 296725211,
         "address": {"road": "Caminho Portugues", "village": "Atalaia",
                     "municipality": "Vila Nova da Barquinha", "county": "Santarem",
                     "country": "Portugal", "country_code": "pt"}}
        """;

    private const string InTheNetherlands = """
        {"address": {"road": "Zeeasterweg", "town": "Dronten", "state": "Flevoland",
                     "country": "Netherlands", "country_code": "nl"}}
        """;

    private const string OnMarthasVineyard = """
        {"address": {"village": "West Tisbury", "county": "Dukes County", "state": "Massachusetts",
                     "country": "United States", "country_code": "us"}}
        """;

    private const string DivisionsNamedSantarem = """
        {"data": [
          {"code": 2409, "admin0": "Portugal", "admin1": "Santarem"},
          {"code": 8836, "admin0": "Brazil", "admin1": "Para", "admin2": "Santarem"},
          {"code": 9040, "admin0": "Brazil", "admin1": "Paraiba", "admin2": "Santarem"},
          {"code": 24889, "admin0": "Portugal", "admin1": "Santarem", "admin2": "Santarem"},
          {"code": 8837, "admin0": "Brazil", "admin1": "Para", "admin2": "Santarem Novo"}]}
        """;

    private const string DivisionsNamedMassachusetts = """
        {"data": [{"code": 3235, "admin0": "United States of America", "admin1": "Massachusetts"}]}
        """;

    private const string NoDivisionOfThatName = """{"data": []}""";

    [Fact]
    public void APositionIsReadAsTheCountryAndTheAreaAroundIt()
    {
        var position = HazardDivision.PositionIn(InPortugal);

        position!.Country.Should().Be("Portugal");
        position.AreaNames.Should().Equal("Santarem");
    }

    // The county, because the same name at two levels is graded differently and the finer of the two is
    // the land the site is actually in.
    [Fact]
    public void TheCountyIsSearchedBeforeTheStateThatHoldsIt()
    {
        HazardDivision.PositionIn(OnMarthasVineyard)!.AreaNames
            .Should().Equal("Dukes County", "Massachusetts");
    }

    // A geocoder names no county for every country. Left out, a Dutch site would be searched by nothing.
    [Fact]
    public void APositionWithNoCountyIsSearchedByItsState()
    {
        HazardDivision.PositionIn(InTheNetherlands)!.AreaNames.Should().Equal("Flevoland");
    }

    // A search needs both halves: without a country every country's divisions are kept, and without a name
    // there is nothing to search for.
    [Theory]
    [InlineData("""{"address": {"county": "Santarem"}}""")]
    [InlineData("""{"address": {"country": "Portugal"}}""")]
    [InlineData("""{"error": "Unable to geocode"}""")]
    [InlineData("not json at all")]
    public void APositionMissingEitherHalfIsNoPosition(string reply)
    {
        HazardDivision.PositionIn(reply).Should().BeNull();
    }

    [Fact]
    public void ADivisionIsReadAsItsCodeAndTheLevelsItSitsUnder()
    {
        var divisions = HazardDivision.DivisionsIn(DivisionsNamedSantarem);

        divisions.Should().HaveCount(5);
        divisions[0].Code.Should().Be("2409");
        divisions[0].Levels.Should().Equal("Portugal", "Santarem");
        divisions[3].FullName.Should().Be("Portugal / Santarem / Santarem");
    }

    // Neither can address a call, and neither can be told from another country's.
    [Theory]
    [InlineData("""{"data": [{"admin0": "Portugal", "admin1": "Santarem"}]}""")]
    [InlineData("""{"data": [{"code": 2409}]}""")]
    [InlineData("""{"data": [{"code": 2409, "admin0": null}]}""")]
    [InlineData("""{"data": [{"code": 2409, "admin0": "   "}]}""")]
    public void ADivisionNamingNoCodeOrNoCountryIsLeftOut(string reply)
    {
        HazardDivision.DivisionsIn(reply).Should().BeEmpty();
    }

    // A level the provider left empty is a level it did not answer, so the division ends there rather than
    // closing up — a name read at the wrong level would order two divisions wrongly.
    [Fact]
    public void ADivisionWhoseDeeperLevelIsEmptyEndsAtTheLevelBeforeIt()
    {
        var division = HazardDivision
            .DivisionsIn("""{"data": [{"code": 2409, "admin0": "Portugal", "admin1": "Santarem", "admin2": null}]}""")
            .Single();

        division.Levels.Should().Equal("Portugal", "Santarem");
    }

    // A provider answering an array, or a page of HTML, is not a reply either reader can take anything
    // from — and taking nothing must not throw the run.
    [Theory]
    [InlineData("[]")]
    [InlineData("\"a message\"")]
    public void AReplyThatIsNoObjectReadsAsNothing(string reply)
    {
        HazardDivision.PositionIn(reply).Should().BeNull();
        HazardDivision.DivisionsIn(reply).Should().BeEmpty();
    }

    [Theory]
    [InlineData(NoDivisionOfThatName)]
    [InlineData("""{"detail": "Not found"}""")]
    [InlineData("not json at all")]
    public void AReplyHoldingNoDivisionsReadsAsNone(string reply)
    {
        HazardDivision.DivisionsIn(reply).Should().BeEmpty();
    }

    // The region and the district inside it are graded differently, so the district — the land the site is
    // actually in — is the one taken.
    [Fact]
    public void TheFinestDivisionOfTheSitesOwnCountryIsTaken()
    {
        var finest = HazardDivision.FinestIn(HazardDivision.DivisionsIn(DivisionsNamedSantarem), "Portugal");

        finest!.Code.Should().Be("24889");
        finest.FullName.Should().Be("Portugal / Santarem / Santarem");
    }

    // Two of the three Brazilian divisions stand deeper than the Portuguese one taken above, so a run that
    // ignored the country would grade a Portuguese site in Brazil.
    [Fact]
    public void ADivisionOfAnotherCountryIsNeverTaken()
    {
        HazardDivision.FinestIn(HazardDivision.DivisionsIn(DivisionsNamedSantarem), "Portugal")!
            .Country.Should().Be("Portugal");
    }

    // The two providers do not agree on a country's full name.
    [Fact]
    public void ACountryOneProviderNamesMoreFullyThanTheOtherStillMatches()
    {
        HazardDivision.FinestIn(HazardDivision.DivisionsIn(DivisionsNamedMassachusetts), "United States")!
            .Code.Should().Be("3235");
    }

    [Fact]
    public void NoDivisionInTheSitesCountryResolvesToNone()
    {
        HazardDivision.FinestIn(HazardDivision.DivisionsIn(DivisionsNamedSantarem), "Spain")
            .Should().BeNull();
    }

    // Nothing here can settle which of the two is the land, and a guessed division reads exactly like a
    // resolved one.
    [Fact]
    public void TwoDivisionsStandingEquallyDeepInTheCountryResolveToNone()
    {
        const string bothNamedSantarem = """
            {"data": [
              {"code": 8836, "admin0": "Brazil", "admin1": "Para", "admin2": "Santarem"},
              {"code": 9040, "admin0": "Brazil", "admin1": "Paraiba", "admin2": "Santarem"}]}
            """;

        HazardDivision.FinestIn(HazardDivision.DivisionsIn(bothNamedSantarem), "Brazil").Should().BeNull();
    }
}
