namespace vos.Service.Stomata.Services;

public sealed record ParticulateLimits(double TenMicrograms, double TwoAndAHalfMicrograms);

public static class WindowsOpen
{
    public static IReadOnlyList<double?> Of(IReadOnlyList<ClimateHour> hours, ParticulateLimits limits) =>
    [
        .. hours.Select(hour => hour is { ParticulateMatterTenMicrograms: { } ten, ParticulateMatterTwoAndAHalfMicrograms: { } twoAndAHalf }
            ? ten <= limits.TenMicrograms && twoAndAHalf <= limits.TwoAndAHalfMicrograms ? 1.0 : 0.0
            : (double?)null),
    ];
}
