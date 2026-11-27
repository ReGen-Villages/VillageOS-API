namespace vos.Service.Shared;

// Whole series go together where they fit, so a request rewrites each stored series it touches once.
public static class DepositRequests
{
    public static IReadOnlyList<IReadOnlyList<ObservationSample>> Of(IEnumerable<ObservationSample> readings, int perRequest)
    {
        var requests = new List<IReadOnlyList<ObservationSample>>();
        var request = new List<ObservationSample>();

        void StartAnother()
        {
            requests.Add(request);
            request = new List<ObservationSample>();
        }

        foreach (var series in readings.GroupBy(reading => reading.Property, StringComparer.Ordinal)
                     .OrderBy(series => series.Key, StringComparer.Ordinal))
        {
            var inTimeOrder = series.OrderBy(reading => reading.ObservedAt).ToList();
            if (request.Count > 0 && request.Count + inTimeOrder.Count > perRequest) StartAnother();
            foreach (var reading in inTimeOrder)
            {
                if (request.Count == perRequest) StartAnother();
                request.Add(reading);
            }
        }
        if (request.Count > 0) requests.Add(request);
        return requests;
    }
}
