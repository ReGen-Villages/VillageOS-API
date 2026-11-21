using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Service.Intake.Services;
using vos.Service.Shared.Geometry;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.Service.Intake.Tests;

public class AreaMethodReaderTests
{
    [Fact]
    public void A_model_measuring_on_the_mean_sphere_answers_that_method_and_the_predicate_a_parcel_names_it_along()
    {
        var model = DeclaredModel.Seeded();

        var choice = AreaMethodReader.Read(model.Build());

        choice.Formula.Should().Be(AreaMethod.MeanSphere);
        choice.RoleWord.Should().Be(LandIntakeRoles.MeanSphereAreaMethod.Word);
        choice.Method.Should().Be(new DeclaredTerm(DeclaredModel.MeanSphere, model.Id(DeclaredModel.MeanSphere)));
        choice.AreaMeasuredWith.Should().Be(new DeclaredTerm("areaMeasuredWith", model.Id("areaMeasuredWith")));
    }

    [Fact]
    public void A_model_measuring_on_the_ellipsoid_answers_that_method()
    {
        var model = DeclaredModel.Seeded()
            .Unrelate("Parcel", "measuresAreaWith")
            .Relate("Parcel", "measuresAreaWith", DeclaredModel.Wgs84Ellipsoid);

        var choice = AreaMethodReader.Read(model.Build());

        choice.Formula.Should().Be(AreaMethod.Wgs84Ellipsoid);
        choice.RoleWord.Should().Be(LandIntakeRoles.Wgs84EllipsoidAreaMethod.Word);
        choice.Method.Id.Should().Be(model.Id(DeclaredModel.Wgs84Ellipsoid));
    }

    [Fact]
    public void A_model_whose_parcel_archetype_chooses_no_method_is_refused_saying_so()
    {
        var model = DeclaredModel.Seeded().Unrelate("Parcel", "measuresAreaWith");

        var reading = () => AreaMethodReader.Read(model.Build());

        reading.Should().Throw<ModelNotSeededError>().WithMessage("*measuresAreaWith*no method*");
    }

    [Fact]
    public void A_model_whose_parcel_archetype_chooses_two_methods_is_refused_naming_both()
    {
        var model = DeclaredModel.Seeded().Relate("Parcel", "measuresAreaWith", DeclaredModel.Wgs84Ellipsoid);

        var reading = () => AreaMethodReader.Read(model.Build());

        reading.Should().Throw<ModelNotSeededError>()
            .WithMessage($"*'{DeclaredModel.MeanSphere}'*'{DeclaredModel.Wgs84Ellipsoid}'*");
    }

    [Fact]
    public void A_method_no_role_this_service_implements_binds_is_refused_by_name()
    {
        var model = DeclaredModel.Seeded()
            .With("Flat plane")
            .Unrelate("Parcel", "measuresAreaWith")
            .Relate("Parcel", "measuresAreaWith", "Flat plane");

        var reading = () => AreaMethodReader.Read(model.Build());

        reading.Should().Throw<ModelNotSeededError>().WithMessage("*'Flat plane'*");
    }

    [Fact]
    public void The_vocabulary_a_submission_is_composed_against_carries_the_method()
    {
        var model = DeclaredModel.Seeded()
            .Unrelate("Parcel", "measuresAreaWith")
            .Relate("Parcel", "measuresAreaWith", DeclaredModel.Wgs84Ellipsoid);

        DeclaredVocabularyReader.Read(model.Build()).AreaMethod.Formula.Should().Be(AreaMethod.Wgs84Ellipsoid);
    }

    [Fact]
    public void The_vocabulary_read_asks_for_every_role_the_area_method_is_found_through()
    {
        var asked = DeclaredVocabularyReader.Selector().Ids!;

        asked.Should().Contain(AreaMethodReader.Roles.Select(role => role.Id));
    }
}
