using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace vos.Service.Shared.Hosting;

// Keeps this service's model clock on the broker's.
//
// Listens rather than polls: the broker announces a change of anchor or rate on its system-events
// stream, and between changes the clock needs nothing, since it maps elapsed real time through the
// rate it was given. Each time the stream is opened the clock is read once, so a change made while
// the stream was down is taken on reconnect.
internal sealed class ModelClockFollower : BackgroundService
{
    private readonly ModelClock _clock;
    private readonly Func<CancellationToken, IAsyncEnumerable<ModelTimeReading>> _listen;
    private readonly ILogger _logger;
    private readonly string _serviceName;
    private readonly TimeSpan _reconnectDelay;
    private TimeSpan? _said;
    private bool _unreachable;

    // Its own logger rather than the static one every service writes through: two tests that each
    // swapped that static raced, and what one captured depended on what another was emitting.
    public ModelClockFollower(
        ModelClock clock,
        Func<CancellationToken, IAsyncEnumerable<ModelTimeReading>> listen,
        ILogger logger,
        string serviceName,
        TimeSpan reconnectDelay)
    {
        _clock = clock;
        _listen = listen;
        _logger = logger;
        _serviceName = serviceName;
        _reconnectDelay = reconnectDelay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ListenOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_reconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // A connection that yields nothing is a broker that could not be reached.
    internal async Task ListenOnceAsync(CancellationToken cancellationToken)
    {
        var heard = false;
        await foreach (var reading in _listen(cancellationToken).WithCancellation(cancellationToken))
        {
            heard = true;
            if (_unreachable)
                _logger.LogInformation("{Service} is reading the model clock again", _serviceName);
            _unreachable = false;

            _clock.AnchorTo(reading.Now, reading.Rate);
            Say(reading);
        }

        if (heard || cancellationToken.IsCancellationRequested) return;

        // Once, not once a reconnect: a broker away for an hour would otherwise write the same warning
        // many hundred times, and the clock goes on answering from its last reading.
        if (!_unreachable)
            _logger.LogWarning("{Service} cannot read the model clock; it stamps the last reading it "
                               + "took until the broker answers again", _serviceName);
        _unreachable = true;
    }

    // Said when the distance between the two clocks changes, not on every reading: a reconnect to a
    // clock that has not moved against this machine would otherwise write the same line again. A
    // second past the last figure is a clock that was re-anchored.
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
