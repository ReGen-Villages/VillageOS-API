namespace vos.Hypha.Registry;

public sealed class RequiresBoundRoleAttribute(string roleIdentifier) : ListingConditionAttribute
{
    public Guid RoleIdentifier { get; } = Guid.Parse(roleIdentifier);

    public override async ValueTask<bool> HoldsForAsync(Caller caller, IServiceProvider services) =>
        await caller.BindsAsync(RoleIdentifier);
}
