namespace vos.Service.Metabolism.Configuration;

/// <summary>Which way an instance of this service moves a quantity, and everything that follows from
/// it. The direction was a word each reader compared against its own literal, so a misspelling read as
/// the other direction and quietly reversed the arithmetic. Here it is one table with two rows.</summary>
public sealed record ResourceDirection(
    string LaunchArgument,
    string ProgressVerb,
    string PoolAction,
    string TrackingPropertyName)
{
    public static readonly ResourceDirection Consumes =
        new("consumes", "decrementing", "decrements", "total_consumed");

    public static readonly ResourceDirection Produces =
        new("produces", "incrementing", "increments", "total_produced");

    public static readonly IReadOnlyList<ResourceDirection> All = [Consumes, Produces];

    /// <summary>The direction the given launch argument names, or null where it names none.</summary>
    public static ResourceDirection? Parse(string? argument) =>
        All.FirstOrDefault(direction =>
            string.Equals(direction.LaunchArgument, argument?.Trim(), StringComparison.OrdinalIgnoreCase));

    public override string ToString() => LaunchArgument;
}
