namespace vos.Service.Stomata.Services;

public sealed record ParticulateLimits(double TenMicrograms, double TwoAndAHalfMicrograms);

public static class WindowsOpen
{
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    public static IReadOnlyList<double?> OnHourlyAir(IReadOnlyList<ClimateHour> hours, ParticulateLimits limits) =>
        [.. hours.Select(hour => Within(hour.ParticulateMatterTenMicrograms, hour.ParticulateMatterTwoAndAHalfMicrograms, limits))];

    // The day is the 24 hours of time up to and including the hour, so readings at uneven instants count by when they fell.
    public static IReadOnlyList<double?> OnDailyAir(IReadOnlyList<ClimateHour> hours, ParticulateLimits limits, int leastReadings)
    {
        var ten = new RunningMean(leastReadings);
        var twoAndAHalf = new RunningMean(leastReadings);
        var open = new List<double?>(hours.Count);
        var oldest = 0;
        foreach (var hour in hours)
        {
            for (; hours[oldest].At <= hour.At - Day; oldest++)
            {
                ten.Remove(hours[oldest].ParticulateMatterTenMicrograms);
                twoAndAHalf.Remove(hours[oldest].ParticulateMatterTwoAndAHalfMicrograms);
            }
            ten.Add(hour.ParticulateMatterTenMicrograms);
            twoAndAHalf.Add(hour.ParticulateMatterTwoAndAHalfMicrograms);
            open.Add(Within(ten.Mean, twoAndAHalf.Mean, limits));
        }
        return open;
    }

    private static double? Within(double? ten, double? twoAndAHalf, ParticulateLimits limits) =>
        ten is { } tenMicrograms && twoAndAHalf is { } twoAndAHalfMicrograms
            ? tenMicrograms <= limits.TenMicrograms && twoAndAHalfMicrograms <= limits.TwoAndAHalfMicrograms ? 1.0 : 0.0
            : null;

    private sealed class RunningMean(int leastReadings)
    {
        private double _sum;
        private int _count;

        public double? Mean => _count >= leastReadings && _count > 0 ? _sum / _count : null;

        public void Add(double? reading)
        {
            if (reading is not { } value) return;
            _sum += value;
            _count++;
        }

        public void Remove(double? reading)
        {
            if (reading is not { } value) return;
            _sum -= value;
            _count--;
        }
    }
}
