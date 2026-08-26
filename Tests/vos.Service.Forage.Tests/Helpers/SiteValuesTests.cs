using System.Text.Json;
using FluentAssertions;
using vos.Service.Forage.Helpers;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Forage.Tests.Helpers;

public class SiteValuesTests
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
    public void Of_ReturnsTheSitesOwnValues()
    {
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId, new Dictionary<string, SnapshotProperty>
        {
            ["lat"] = Property("-25.75"),
            ["lng"] = Property("28.19"),
            ["climateZone"] = Property("\"Csa\""),
        }));

        var values = SiteValues.Of(snapshot, siteId);

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

        var values = SiteValues.Of(snapshot, siteId);

        values["lng"].Should().Be("28.19");
        values["elevation"].Should().Be("1200");
    }

    [Fact]
    public void Of_LeavesOutInheritedValues()
    {
        // A value inherited from an archetype is a default for a kind of site. Calling a provider
        // with a default location would return a confident reading about somewhere else.
        var siteId = Guid.NewGuid();
        var snapshot = Snapshot(Site(siteId,
            own: new Dictionary<string, SnapshotProperty> { ["lat"] = Property("-25.75") },
            inherited: new Dictionary<string, InheritedPropertySet>
            {
                ["SiteArchetype"] = new("SiteArchetype", new Dictionary<string, SnapshotProperty>
                {
                    ["lng"] = Property("0"),
                }),
            }));

        var values = SiteValues.Of(snapshot, siteId);

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

        var values = SiteValues.Of(snapshot, siteId);

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

        SiteValues.Of(snapshot, siteId)["coastal"].Should().Be("true");
    }

    [Fact]
    public void Of_SiteNotInTheSnapshot_ReturnsNoValues()
    {
        var values = SiteValues.Of(Snapshot(Site(Guid.NewGuid())), Guid.NewGuid());

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

        SiteValues.Of(snapshot, siteId).ContainsKey("LAT").Should().BeTrue();
    }
}
