using System.Text.Json;
using FluentAssertions;
using vos.Service.Intake.Helpers;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Subscriptions;
using Xunit;

namespace vos.Service.Intake.Tests;

public class SiteElevationReaderTests
{
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SiteArchetypeId = Guid.NewGuid();
    private static readonly Guid ParcelArchetypeId = Guid.NewGuid();

    private static readonly IReadOnlyList<BoundaryPoint> Border =
    [
        new() { Latitude = 25.00, Longitude = 55.40 }, new() { Latitude = 25.00, Longitude = 55.44 },
        new() { Latitude = 25.04, Longitude = 55.44 }, new() { Latitude = 25.04, Longitude = 55.40 },
    ];

    private static readonly IReadOnlyList<BoundaryPoint> MovedBorder =
        [.. Border.Select(corner => new BoundaryPoint { Latitude = corner.Latitude + 0.01, Longitude = corner.Longitude })];

    private static SnapshotProperty Value(object value) => new(JsonSerializer.SerializeToElement(value), null, null);

    // How the platform holds a value written for a name the Thing's archetype declares: in an override set under
    // that archetype, not among the Thing's own properties.
    private static SnapshotThing Stating(Guid id, Guid archetypeId, Dictionary<string, SnapshotProperty> values) =>
        new(id, null, false, [], new Dictionary<string, InheritedPropertySet>
        {
            [archetypeId.ToString()] = new(null, values, null),
        }, [], null);

    private static SnapshotDocument Holding(params SnapshotThing[] things) => new(0, [.. things], []);

    private static SnapshotThing ParcelWith(IReadOnlyList<BoundaryPoint> border) =>
        Stating(ParcelId, ParcelArchetypeId, new() { ["boundary"] = Value(BoundaryGeometry.ToGeoJson(border)) });

    private static SnapshotThing SiteWith(params string[] figures) =>
        Stating(SiteId, SiteArchetypeId, figures.ToDictionary(figure => figure, _ => Value(30.0)));

    [Fact]
    public void A_site_holding_both_figures_for_the_posted_border_is_not_looked_up_again()
    {
        var held = SiteElevationReader.Read(
            Holding(ParcelWith(Border), SiteWith("elevationMinimumMetres", "elevationMaximumMetres")), SiteId, ParcelId);

        held.NeedsLookupFor(Border).Should().BeFalse();
    }

    [Fact]
    public void A_moved_border_is_looked_up_again()
    {
        var held = SiteElevationReader.Read(
            Holding(ParcelWith(Border), SiteWith("elevationMinimumMetres", "elevationMaximumMetres")), SiteId, ParcelId);

        held.NeedsLookupFor(MovedBorder).Should().BeTrue();
    }

    [Fact]
    public void A_site_missing_either_figure_is_looked_up_again()
    {
        var held = SiteElevationReader.Read(Holding(ParcelWith(Border), SiteWith("elevationMaximumMetres")), SiteId, ParcelId);

        held.NeedsLookupFor(Border).Should().BeTrue();
        held.FiguresStated.Should().Equal("elevationMaximumMetres");
    }

    [Fact]
    public void A_first_submission_is_looked_up()
    {
        var held = SiteElevationReader.Read(Holding(), SiteId, ParcelId);

        held.Boundary.Should().BeNull();
        held.FiguresStated.Should().BeEmpty();
        held.NeedsLookupFor(Border).Should().BeTrue();
    }

    // The archetype declares the figures too, and a reader resolving up the `is` chain would find a declaration
    // there for a site that has stated nothing.
    [Fact]
    public void A_figure_the_site_has_not_stated_is_not_read_from_its_archetype()
    {
        var archetype = new SnapshotThing(SiteArchetypeId, "Site", true,
            new() { ["elevationMinimumMetres"] = Value(1.0), ["elevationMaximumMetres"] = Value(2.0) }, null, [], null);

        var held = SiteElevationReader.Read(Holding(archetype, ParcelWith(Border), SiteWith()), SiteId, ParcelId);

        held.FiguresStated.Should().BeEmpty();
    }

    [Theory]
    [InlineData(84.0, null)]
    [InlineData(null, 112.0)]
    public void An_answer_holding_one_end_of_the_range_is_no_range(double? lowest, double? highest)
    {
        new AnsweredElevation(100, lowest, highest, null).Range.Should().BeNull();
    }

    [Fact]
    public void The_read_asks_for_the_site_and_parcel_alone()
    {
        var selector = SiteElevationReader.Selector(SiteId, ParcelId);

        selector.Ids.Should().Equal(SiteId, ParcelId);
        selector.IncludeIsAncestors.Should().BeFalse();
        selector.IncludeRelationships.Should().BeFalse();
    }
}
