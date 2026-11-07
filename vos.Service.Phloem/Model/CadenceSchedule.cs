namespace vos.Service.Phloem.Model;

public sealed record Cadence(Guid Id, string Name, double IntervalMinutes, DateTimeOffset? LastDue);

public static class CadenceSchedule
{
    // Instants before coveredFrom were jumped over by a clock moved ahead, or missed while the orchestrator was away,
    // so only the latest of them runs rather than one run for each.
    public static IReadOnlyList<DateTimeOffset> DueAt(Cadence cadence, DateTimeOffset now, DateTimeOffset coveredFrom)
    {
        if (cadence.IntervalMinutes <= 0) return [];
        if (cadence.LastDue is not { } last || last > now) return [now];

        var interval = TimeSpan.FromMinutes(cadence.IntervalMinutes);
        var latest = (long)Math.Floor((now - last) / interval);
        if (latest < 1) return [];

        var earliestCovered = Math.Max(1, (long)Math.Floor((coveredFrom - last) / interval) + 1);
        if (earliestCovered > latest) return [last + latest * interval];

        var due = new List<DateTimeOffset>();
        for (var step = earliestCovered; step <= latest; step++)
            due.Add(last + step * interval);
        return due;
    }
}
