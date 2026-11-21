using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Geometry;
using Xunit;

namespace vos.Service.Intake.Tests;

public class ParcelAreaCompositionTests
{
    private static ComposedSubmission ComposedMeasuringWith(AreaMethodChoice method) =>
        SubmissionFragmentComposer.Compose(
            WillowBend.Submission(), WillowBend.KnownPredicates, WillowBend.KnownArchetypes,
            WillowBend.KnownVocabulary with { AreaMethod = method }, WillowBend.ArrivedAt, elevation: null);

    private static IReadOnlyList<(double Latitude, double Longitude)> SubmittedBorder =>
        [.. WillowBend.Submission().Parcel!.Boundary!.Select(corner => (corner.Latitude, corner.Longitude))];

    private static double MeasuredArea(ComposedSubmission composed) =>
        (double)composed.Fragment.Things.Single(thing => thing.Id == composed.ParcelId).Properties["measuredAreaHectares"].Value!;

    [Theory]
    [InlineData(AreaMethod.MeanSphere)]
    [InlineData(AreaMethod.Wgs84Ellipsoid)]
    public void The_parcel_is_measured_with_the_method_the_model_configures(AreaMethod formula)
    {
        var method = formula == AreaMethod.MeanSphere ? WillowBend.MeasuredOnTheMeanSphere : WillowBend.MeasuredOnTheEllipsoid;

        MeasuredArea(ComposedMeasuringWith(method)).Should().Be(BorderArea.Hectares(SubmittedBorder, formula));
    }

    [Fact]
    public void The_two_methods_give_the_parcel_different_areas()
    {
        MeasuredArea(ComposedMeasuringWith(WillowBend.MeasuredOnTheEllipsoid))
            .Should().NotBe(MeasuredArea(ComposedMeasuringWith(WillowBend.MeasuredOnTheMeanSphere)));
    }

    [Fact]
    public void A_parcel_amended_after_a_change_of_method_names_only_the_method_now_configured()
    {
        var before = WillowBend.MeasuredOnTheMeanSphere;
        var after = WillowBend.MeasuredOnTheEllipsoid;

        var amended = ComposedMeasuringWith(after);

        amended.Fragment.Relationships.Should().NotContain(edge =>
            edge.Subject == amended.ParcelId && edge.Predicate == after.AreaMeasuredWith.Id && edge.Target == before.Method.Id);
        amended.Fragment.Retractions.Should().ContainSingle(edge =>
            edge.Subject == amended.ParcelId && edge.Predicate == after.AreaMeasuredWith.Id && edge.Target == before.Method.Id);
        amended.Fragment.Retractions.Should().NotContain(edge => edge.Target == after.Method.Id);
    }

    [Fact]
    public void The_parcel_names_the_method_its_area_was_measured_with_in_the_same_fragment()
    {
        var method = WillowBend.MeasuredOnTheEllipsoid;

        var composed = ComposedMeasuringWith(method);

        composed.Fragment.Relationships.Should().ContainSingle(edge =>
            edge.Subject == composed.ParcelId && edge.Predicate == method.AreaMeasuredWith.Id && edge.Target == method.Method.Id);
    }
}
