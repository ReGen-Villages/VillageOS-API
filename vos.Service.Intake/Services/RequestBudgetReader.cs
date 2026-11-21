using vos.Service.Shared.Roles;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// Falls back rather than refusing, because refusing every request would take the public site down; VillageOS Task 8694's bounds replace these checks.
public static class RequestBudgetReader
{
    public const string RequestCountProperty = "requestCount";
    public const string WindowSecondsProperty = "windowSeconds";

    public static readonly TimeSpan ShortestWindow = TimeSpan.FromSeconds(60);

    public static readonly IReadOnlyList<Role> Roles = [LandIntakeRoles.VerifiedRequestBudget, LandIntakeRoles.UnverifiedRequestBudget];

    public static SubscriptionSelector Selector() => new()
    {
        Ids = RoleBindings.Identifiers(Roles),
        Names = [RoleBindings.BindsPredicateName],
        Traverse = [RoleBindings.FollowBindings],
        IncludeRelationships = true,
    };

    public static DeclaredRequestBudgets Read(SnapshotDocument snapshot)
    {
        var fallbackReasons = new List<string>();
        var unverified = Declared(snapshot, LandIntakeRoles.UnverifiedRequestBudget, fallbackReasons);
        var verified = Declared(snapshot, LandIntakeRoles.VerifiedRequestBudget, fallbackReasons);

        if (verified.RequestCount < unverified.RequestCount)
        {
            fallbackReasons.Add(
                $"the {LandIntakeRoles.VerifiedRequestBudget.Word} allows {verified.RequestCount} requests, fewer than the "
                + $"{unverified.RequestCount} a request without a ticket is allowed, so {Described(RequestBudget.Fallback)} applies to it.");
            verified = RequestBudget.Fallback;
        }

        return new DeclaredRequestBudgets(verified, unverified, fallbackReasons);
    }

    private static RequestBudget Declared(SnapshotDocument snapshot, Role role, List<string> fallbackReasons)
    {
        var bound = snapshot.Bound(role);
        if (bound.Count != 1)
            return FallBack(fallbackReasons, bound.Count == 0
                ? $"the model binds nothing to the role {role.Word}"
                : $"the model binds the role {role.Word} to more than one Thing — {string.Join(", ", bound.Select(budget => $"'{budget.Name}'"))}");

        var budget = bound[0];
        var count = budget.StatedWholeNumber(RequestCountProperty);
        var seconds = budget.StatedWholeNumber(WindowSecondsProperty);
        if (count is not (> 0 and <= int.MaxValue))
            return FallBack(fallbackReasons, $"'{budget.Name}', the {role.Word}, states {RequestCountProperty} {count?.ToString() ?? "nothing"}, which is not a count of requests");
        if (seconds is null || seconds < ShortestWindow.TotalSeconds)
            return FallBack(fallbackReasons, $"'{budget.Name}', the {role.Word}, states {WindowSecondsProperty} {seconds?.ToString() ?? "nothing"}, under the {ShortestWindow.TotalSeconds} seconds a window must last");

        return new RequestBudget((int)count, TimeSpan.FromSeconds(seconds.Value));
    }

    private static RequestBudget FallBack(List<string> fallbackReasons, string why)
    {
        fallbackReasons.Add($"{why}, so {Described(RequestBudget.Fallback)} applies to it.");
        return RequestBudget.Fallback;
    }

    private static string Described(RequestBudget budget) => $"{budget.RequestCount} requests in {budget.Window.TotalSeconds} seconds";
}
