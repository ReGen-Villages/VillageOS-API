using vos.Service.Intake.Models;
using vos.Service.Shared.Geometry;
using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public static class AreaMethodReader
{
    public static readonly Role[] Roles =
    [
        LandIntakeRoles.Parcel, LandIntakeRoles.MeasuresAreaWith, LandIntakeRoles.AreaMeasuredWith,
        LandIntakeRoles.MeanSphereAreaMethod, LandIntakeRoles.Wgs84EllipsoidAreaMethod,
    ];

    public static AreaMethodChoice Read(SnapshotDocument snapshot) => new(
        new DeclaredTerm(string.Empty, Guid.Empty), AreaMethod.MeanSphere, LandIntakeRoles.MeanSphereAreaMethod.Word,
        new DeclaredTerm(string.Empty, Guid.Empty));
}
