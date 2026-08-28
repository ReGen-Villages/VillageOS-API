using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

public class StatedValuesTests
{
    private static SnapshotProperty Property(string json) =>
        new(JsonDocument.Parse(json).RootElement, null, null);

    private static SnapshotThing Site(Guid id, Dictionary<string, SnapshotProperty>? own = null,
        Dictionary<string, InheritedPropertySet>? inherited = null) => new(
        id, "WillowBend", false,
        own ?? new Dictionary<string, SnapshotProperty>(),
        inherited ?? new Dictionary<string, InheritedPropertySet>(),
        Array.Empty<string>(),
        Array.Empty<Guid>());

    private static SnapshotDocument Snapshot(params SnapshotThing[] things) =>
        new(0, things.ToList(), new List<SnapshotRelationship>());

    [Fact]
    public void Of_ReturnsTheSitesStatedValues()
    {
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId, new Dictionary<string, SnapshotProperty>
        {
            ["lat"] = Property("-25.75"),
            ["lng"] = Property("28.19"),
            ["climateZone"] = Property("\"Csa\""),
        }));

        var values = StatedValues.Of(snapshot, siteId);

        values["lat"].Should().Be("-25.75");
        values["lng"].Should().Be("28.19");
        values["climateZone"].Should().Be("Csa");
    }

    [Fact]
    public void Of_NumbersKeepTheDigitsTheSourceHeld()
    {
        // Reformatting through this process's culture could turn 28.19 into 28,19 and send that to a
        // provider, which would either fail or silently mean a different place.
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId, new Dictionary<string, SnapshotProperty>
        {
            ["lng"] = Property("28.19"),
            ["elevation"] = Property("1200"),
        }));

        var values = StatedValues.Of(snapshot, siteId);

        values["lng"].Should().Be("28.19");
        values["elevation"].Should().Be("1200");
    }

    /// <summary>A submitted site states its coordinates over the names its archetype declares, so the
    /// model stores them as overrides and its own properties are empty. Read as inherited defaults and
    /// left out, every submitted site addressed its sources with nothing (#6805).</summary>
    [Fact]
    public void Of_CarriesWhatTheSiteStatesOverItsArchetypesDeclaration()
    {
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId,
            inherited: new Dictionary<string, InheritedPropertySet>
            {
                ["SiteArchetype"] = new("Site", new Dictionary<string, SnapshotProperty>
                {
                    ["lat"] = Property("-25.75"),
                    ["lng"] = Property("28.19"),
                }, null),
            }));

        var values = StatedValues.Of(snapshot, siteId);

        values["lat"].Should().Be("-25.75");
        values["lng"].Should().Be("28.19");
    }

    /// <summary>What the archetype itself carries stays on the archetype. The snapshot puts a value the
    /// instance never wrote nowhere in the instance's payload, so a default for a kind of site cannot
    /// reach a provider as though it were this site's own.</summary>
    [Fact]
    public void Of_LeavesOutWhatOnlyTheArchetypeCarries()
    {
        var siteId = Guid.NewGuid();
        var archetype = new SnapshotThing(
            Guid.NewGuid(), "Site", true,
            new Dictionary<string, SnapshotProperty> { ["lng"] = Property("0") },
            null, Array.Empty<string>(), Array.Empty<Guid>());
        var snapshot = Snapshot(
            Site(siteId, own: new Dictionary<string, SnapshotProperty> { ["lat"] = Property("-25.75") }),
            archetype);

        var values = StatedValues.Of(snapshot, siteId);

        values.Should().ContainKey("lat");
        values.Should().NotContainKey("lng");
    }

    [Fact]
    public void Of_SkipsValuesWithNoTextForm()
    {
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId, new Dictionary<string, SnapshotProperty>
        {
            ["lat"] = Property("-25.75"),
            ["boundary"] = Property("[1,2,3]"),
            ["nothing"] = Property("null"),
            ["blank"] = Property("\"   \""),
        }));

        var values = StatedValues.Of(snapshot, siteId);

        values.Keys.Should().Equal("lat");
    }

    [Fact]
    public void Of_BooleanValuesAreCarried()
    {
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId, new Dictionary<string, SnapshotProperty>
        {
            ["coastal"] = Property("true"),
        }));

        StatedValues.Of(snapshot, siteId)["coastal"].Should().Be("true");
    }

    [Fact]
    public void Of_MarksAreNotValues()
    {
        // A double-underscored property is how a reader finds a Thing, not something true of the
        // place or assessment a call is about — a mark sent as an address value would fill a
        // placeholder no provider means.
        var thingId = Guid.NewGuid();
        var snapshot = Snapshot(Site(thingId, new Dictionary<string, SnapshotProperty>
        {
            ["__IsRootPlace"] = Property("true"),
            ["hazardPortalDivision"] = Property("\"2062\""),
        }));

        var values = StatedValues.Of(snapshot, thingId);

        values.Keys.Should().Equal("hazardPortalDivision");
    }

    [Fact]
    public void Of_SiteNotInTheSnapshot_ReturnsNoValues()
    {
        var values = StatedValues.Of(Snapshot(Site(Guid.NewGuid())), Guid.NewGuid());

        values.Should().BeEmpty();
    }

    [Fact]
    public void Of_NameLookupIsCaseInsensitive()
    {
        // The fetcher matches placeholder names case-insensitively; these values have to agree, or a
        // {Lat} in an address would go unfilled against a lat the site holds.
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId, new Dictionary<string, SnapshotProperty>
        {
            ["lat"] = Property("-25.75"),
        }));

        StatedValues.Of(snapshot, siteId).ContainsKey("LAT").Should().BeTrue();
    }
}
