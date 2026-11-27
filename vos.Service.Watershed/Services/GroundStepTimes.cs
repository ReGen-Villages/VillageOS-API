namespace vos.Service.Watershed.Services;

internal sealed record GroundStepPart(string Name, TimeSpan Taken, int Calls, bool UnderWay);

internal sealed class GroundStepTimes(TimeProvider clock)
{
    private readonly long _startedAt = clock.GetTimestamp();
    private readonly List<GroundStepPart> _parts = [];
    private (string Part, long StartedAt)? _underWay;

    public TimeSpan Total => clock.GetElapsedTime(_startedAt);

    public async Task<T> TimeAsync<T>(string part, Func<Task<T>> call)
    {
        var startedAt = clock.GetTimestamp();
        _underWay = (part, startedAt);
        var answer = await call();
        _underWay = null;
        Add(part, clock.GetElapsedTime(startedAt), calls: 1, underWay: false);
        return answer;
    }

    public Task TimeAsync(string part, Func<Task> call) => TimeAsync(part, async () =>
    {
        await call();
        return true;
    });

    public IReadOnlyList<GroundStepPart> PartsWhenStopped()
    {
        if (_underWay is { } underWay)
        {
            Add(underWay.Part, clock.GetElapsedTime(underWay.StartedAt), calls: 0, underWay: true);
            _underWay = null;
        }
        return _parts;
    }

    private void Add(string part, TimeSpan taken, int calls, bool underWay)
    {
        var index = _parts.FindIndex(existing => existing.Name == part);
        if (index < 0)
            _parts.Add(new GroundStepPart(part, taken, calls, underWay));
        else
            _parts[index] = _parts[index] with { Taken = _parts[index].Taken + taken, Calls = _parts[index].Calls + calls, UnderWay = underWay };
    }
}
