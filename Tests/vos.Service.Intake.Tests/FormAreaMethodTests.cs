using FluentAssertions;
using vos.Service.Intake.Services;
using vos.Service.Shared.Roles;
using Xunit;

namespace vos.Service.Intake.Tests;

// The page measures its live area itself, so the form names the method by the word the platform implements it under.
public class FormAreaMethodTests
{
    [Fact]
    public void A_form_names_the_mean_sphere_where_the_model_measures_on_it()
    {
        FormOptionsReader.Read(DeclaredModel.Seeded().Build()).AreaMethod
            .Should().Be(LandIntakeRoles.MeanSphereAreaMethod.Word);
    }

    [Fact]
    public void A_form_names_the_ellipsoid_where_the_model_measures_on_it()
    {
        var model = DeclaredModel.Seeded()
            .Unrelate("Parcel", "measuresAreaWith")
            .Relate("Parcel", "measuresAreaWith", DeclaredModel.Wgs84Ellipsoid);

        FormOptionsReader.Read(model.Build()).AreaMethod.Should().Be(LandIntakeRoles.Wgs84EllipsoidAreaMethod.Word);
    }

    [Fact]
    public void The_form_read_asks_for_every_role_the_area_method_is_found_through()
    {
        FormOptionsReader.Selector().Ids!.Should().Contain(AreaMethodReader.Roles.Select(role => role.Id));
    }
}
