namespace vos.Service.Stomata.Services;

public sealed record ParticulateLimits(double TenMicrograms, double TwoAndAHalfMicrograms);

public static class WindowsOpen
{
    public static IReadOnlyList<double?> Of(IReadOnlyList<ClimateHour> hours, ParticulateLimits limits) => [];
}
