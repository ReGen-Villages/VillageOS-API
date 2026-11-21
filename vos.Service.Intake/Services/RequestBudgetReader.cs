using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

public static class RequestBudgetReader
{
    public const string RequestCountProperty = "requestCount";
    public const string WindowSecondsProperty = "windowSeconds";

    public static readonly TimeSpan ShortestWindow = TimeSpan.FromSeconds(60);

    public static readonly IReadOnlyList<Role> Roles = [LandIntakeRoles.VerifiedRequestBudget, LandIntakeRoles.UnverifiedRequestBudget];

    public static SubscriptionSelector Selector() => new();

    public static DeclaredRequestBudgets Read(SnapshotDocument snapshot) => DeclaredRequestBudgets.Fallback;
}
