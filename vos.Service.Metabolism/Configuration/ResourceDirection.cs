namespace vos.Service.Metabolism.Configuration;

/// <summary>Which way an instance of this service moves a quantity, and every word that follows from
/// it. One row per direction, so a reader takes a direction and asks it rather than deciding again
/// from the launch word — where anything but an exact match silently means the other direction.</summary>
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

    public static ResourceDirection? Parse(string? argument) =>
        All.FirstOrDefault(direction =>
            string.Equals(direction.LaunchArgument, argument?.Trim(), StringComparison.OrdinalIgnoreCase));

    public override string ToString() => LaunchArgument;
}
