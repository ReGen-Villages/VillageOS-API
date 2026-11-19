namespace vos.Hypha.Registry;

public sealed class RequiresPolicyAttribute(MyceliumPolicy policy) : ListingConditionAttribute
{
    public MyceliumPolicy Policy { get; } = policy;

    public override ValueTask<bool> HoldsForAsync(Caller caller, IServiceProvider services) =>
        ValueTask.FromResult(Policy.IsSatisfiedBy(caller.Role));
}
