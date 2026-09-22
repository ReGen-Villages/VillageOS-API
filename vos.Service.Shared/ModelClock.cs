namespace vos.Service.Shared;

// The clock a service stamps model values with: the broker's clock, not this machine's.
//
// The platform's clock can be anchored — a simulation runs a village day through in minutes, and
// everything the platform itself writes carries the instant the model has reached. A service writing
// `submittedAt` or `resolvedAt` off its own wall clock puts a different year on the same run, so a
// reading that compares an instant a service wrote with one the platform wrote compares two clocks.
//
// The mapping is held locally and read without a round trip: the broker is asked what time the model
// thinks it is and how fast that runs against real time, and every instant after that is worked out
// from the elapsed real time. Left un-anchored — the state a service starts in, and the state it
// stays in wherever the broker cannot be reached — it is the wall clock, which is what the model's
// own clock is when nothing has simulated it.
public sealed class ModelClock : TimeProvider
{
    private sealed record Mapping(DateTimeOffset ModelInstant, DateTimeOffset RealInstant, double Rate);

    private readonly TimeProvider _real;
    private volatile Mapping? _anchor;

    public ModelClock(TimeProvider? real = null) => _real = real ?? System;

    public bool IsAnchored => _anchor is not null;

    public double Rate => _anchor?.Rate ?? 1.0;

    public override DateTimeOffset GetUtcNow()
    {
        var anchor = _anchor;
        return anchor is null
            ? _real.GetUtcNow()
            : anchor.ModelInstant + (_real.GetUtcNow() - anchor.RealInstant) * anchor.Rate;
    }

    /// <summary>How far the model's clock stands from this machine's, as the reading that anchored it
    /// said. Positive where the model is ahead.</summary>
    public TimeSpan OffsetFromWallClock()
    {
        var anchor = _anchor;
        return anchor is null ? TimeSpan.Zero : anchor.ModelInstant - anchor.RealInstant;
    }

    /// <summary>Take the reading the broker gave. The real instant is read here rather than passed in,
    /// so the mapping counts from the moment the answer landed and not from the moment it was asked
    /// for — the round trip is on the side of the clock running behind, which is the side that cannot
    /// stamp an instant the model has not reached yet.</summary>
    public void AnchorTo(DateTimeOffset modelInstant, double rate)
    {
        if (rate < 0) throw new ArgumentOutOfRangeException(nameof(rate), "model time cannot run backwards");
        _anchor = new Mapping(modelInstant, _real.GetUtcNow(), rate);
    }
}
