using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger _logger;
    private readonly string _serviceName;
    private readonly TimeSpan _interval;
    private TimeSpan? _said;
    private bool _unreachable;

    // Its own logger rather than the static one every service writes through: two tests that each
    // swapped that static raced, and what one captured depended on what another was emitting.
    public ModelClockFollower(
        ModelClock clock,
        Func<CancellationToken, Task<ModelTimeReading?>> read,
        ILogger logger,
        string serviceName,
        TimeSpan interval)
    {
        _clock = clock;
        _read = read;
        _logger = logger;
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

    internal async Task FollowOnceAsync(CancellationToken cancellationToken)
    {
        if (await _read(cancellationToken) is not { } reading)
        {
            // Once, not once an interval: a broker away for an hour would otherwise write the same
            // warning several hundred times, and the clock goes on answering from its last reading.
            if (!_unreachable)
                _logger.LogWarning("{Service} cannot read the model clock; it stamps the last reading it "
                                   + "took until the broker answers again", _serviceName);
            _unreachable = true;
            return;
        }

        if (_unreachable)
            _logger.LogInformation("{Service} is reading the model clock again", _serviceName);
        _unreachable = false;

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
        _logger.LogInformation(
            "{Service} stamps the model clock: model {ModelInstant:o}, this machine {WallInstant:o}, "
            + "model {Standing} at {Rate}× real time",
            _serviceName, reading.Now, DateTimeOffset.UtcNow, Standing(offset), reading.Rate);
    }

    // Which way round the two clocks stand, in words: a run of a past day is a model behind this
    // machine, and a figure that can be negative is read wrong as often as right.
    private static string Standing(TimeSpan offset) => offset switch
    {
        _ when offset > TimeSpan.Zero => $"ahead of it by {offset}",
        _ when offset < TimeSpan.Zero => $"behind it by {offset.Duration()}",
        _ => "level with it",
    };
}
