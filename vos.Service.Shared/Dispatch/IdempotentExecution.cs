using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("vos.Service.Shared.Tests")]

namespace vos.Service.Shared.Dispatch;

// Handled-predicate dispatch is at-least-once: the broker sends a relation again when it cannot
// confirm the handler finished, and a handler that reconnects is sent what it missed. Each arrival
// is one call to the handler, and nothing in the call says which arrival it is.
//
// A claim is the note kept here against a relationshipId work has started for. The first arrival
// takes it and runs the effect; a later one finds it taken and does nothing. An effect that throws
// gives its claim back, because an arrival that failed has to run again rather than be swallowed.
//
// Claims are kept only for Retention. This guards against a burst of repeats; it is not a durable
// record of what completed, which belongs on the relation and outlives any one process. Bounding it
// is what stops a handler running for weeks from holding every relationship it ever saw.
public sealed class IdempotentExecution
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(1);

    private sealed class Claim(DateTimeOffset takenAt)
    {
        public DateTimeOffset TakenAt { get; } = takenAt;
    }

    private readonly ConcurrentDictionary<string, Claim> _claims = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    // A relationship that never arrives again would keep its claim forever, so a sweep falls due
    // once every Retention to drop the lapsed ones. Judging a claim as it is asked for, rather than
    // sweeping on every call, is what keeps one arrival costing the same however many are held.
    private long _sweepDueAtTicks;

    public IdempotentExecution(TimeSpan? retention = null, Func<DateTimeOffset>? now = null)
    {
        Retention = retention ?? DefaultRetention;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _sweepDueAtTicks = (_now() + Retention).UtcTicks;
    }

    public TimeSpan Retention { get; }

    public bool IsClaimed(string relationshipId) =>
        _claims.TryGetValue(relationshipId, out var held) && !HasLapsed(held, _now());

    public bool TryClaim(string relationshipId)
    {
        var mine = new Claim(_now());

        var held = _claims.GetOrAdd(relationshipId, mine);
        if (ReferenceEquals(held, mine))
        {
            DropLapsedClaimsIfDue(mine.TakenAt);
            return true;
        }

        return HasLapsed(held, mine.TakenAt) && _claims.TryUpdate(relationshipId, mine, held);
    }

    public void ReleaseClaim(string relationshipId) => _claims.TryRemove(relationshipId, out _);

    internal int HeldClaims => _claims.Count;

    // True when this delivery ran the effect, false when an earlier one already did.
    public async Task<bool> RunOnceAsync(string relationshipId, Func<Task> effect)
    {
        if (!TryClaim(relationshipId))
            return false;

        try
        {
            await effect();
        }
        catch
        {
            ReleaseClaim(relationshipId);
            throw;
        }

        return true;
    }

    private bool HasLapsed(Claim claim, DateTimeOffset now) => now - claim.TakenAt >= Retention;

    private void DropLapsedClaimsIfDue(DateTimeOffset now)
    {
        if (now.UtcTicks < Interlocked.Read(ref _sweepDueAtTicks))
            return;

        Interlocked.Exchange(ref _sweepDueAtTicks, (now + Retention).UtcTicks);

        foreach (var (relationshipId, held) in _claims)
        {
            if (HasLapsed(held, now))
                _claims.TryRemove(new KeyValuePair<string, Claim>(relationshipId, held));
        }
    }
}
