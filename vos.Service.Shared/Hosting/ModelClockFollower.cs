using Microsoft.Extensions.Hosting;
using Serilog;

namespace vos.Service.Shared.Hosting;

// Keeps this service's model clock on the broker's.
//
// Asked again on an interval rather than once at startup: a service is launched while the model is
// still on the wall clock and a simulation anchors the clock afterwards, so a single reading at
// start-up would be the one reading that is certainly wrong. Between readings the clock needs nothing
// — it maps elapsed real time through the rate it was given — so the interval only bounds how long a
// service can go on stamping the clock it was told about last.
internal sealed class ModelClockFollower : BackgroundService
{
    private readonly ModelClock _clock;
    private readonly Func<CancellationToken, Task<ModelTimeReading?>> _read;
    private readonly string _serviceName;
    private readonly TimeSpan _interval;
    private TimeSpan? _said;

    public ModelClockFollower(
        ModelClock clock,
        Func<CancellationToken, Task<ModelTimeReading?>> read,
        string serviceName,
        TimeSpan interval)
    {
        _clock = clock;
        _read = read;
        _serviceName = serviceName;
        _interval = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await FollowOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Internal so a test drives one reading without a timer.
    internal async Task FollowOnceAsync(CancellationToken cancellationToken)
    {
        if (await _read(cancellationToken) is not { } reading)
            return;

        _clock.AnchorTo(reading.Now, reading.Rate);
        Say(reading);
    }

    // Said when the distance between the two clocks changes, not on every reading: a run at sixty
    // times real speed moves the model on between readings, so a line per reading would be a log of
    // the interval. A second past the last figure is a clock that was re-anchored.
    private void Say(ModelTimeReading reading)
    {
        var offset = _clock.OffsetFromWallClock();
        if (_said is { } already && (offset - already).Duration() < TimeSpan.FromSeconds(1))
            return;

        _said = offset;
        Log.Information(
            "{Service} stamps the model clock: model {ModelInstant:o}, this machine {WallInstant:o}, "
            + "model ahead by {Offset} at {Rate}× real time",
            _serviceName, reading.Now, DateTimeOffset.UtcNow, offset, reading.Rate);
    }
}
