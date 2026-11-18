namespace vos.Mcp.Registry;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public abstract class ListingConditionAttribute : Attribute
{
    public abstract ValueTask<bool> HoldsForAsync(Caller caller, IServiceProvider services);
}
