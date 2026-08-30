using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("vos.Service.Shared.Tests")]

namespace vos.Service.Shared.Dispatch;

// Handled-predicate dispatch is at-least-once: the broker sends a relation again when it cannot
// confirm the handler finished, and a handler that reconnects is sent what it missed. A handler
// wraps its side-effect in RunOnceAsync so the repeat does nothing. An effect that throws gives its
// claim back, because a delivery that failed has to run again rather than be swallowed.
//
// Claims are held only for Retention. This is a guard against a burst of repeats, not a durable
// record of what completed — that belongs on the relation, and outlives any one process. Bounding
// it is what stops a handler that runs for weeks from holding every relationship it ever saw.
public sealed class IdempotentExecution
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(1);

    private sealed class Claim(DateTimeOffset takenAt)
    {
        public DateTimeOffset TakenAt { get; } = takenAt;
    }

    private readonly ConcurrentDictionary<string, Claim> _claims = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    public IdempotentExecution(TimeSpan? retention = null, Func<DateTimeOffset>? now = null)
    {
        Retention = retention ?? DefaultRetention;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public TimeSpan Retention { get; }

    public bool IsClaimed(string relationshipId) =>
        _claims.TryGetValue(relationshipId, out var held) && !HasLapsed(held, _now());

    public bool TryClaim(string relationshipId)
    {
        var mine = new Claim(_now());
        DropLapsedClaims(mine.TakenAt);

        var held = _claims.GetOrAdd(relationshipId, mine);
        if (ReferenceEquals(held, mine))
            return true;

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

    private void DropLapsedClaims(DateTimeOffset now)
    {
        foreach (var (relationshipId, held) in _claims)
        {
            if (HasLapsed(held, now))
                _claims.TryRemove(new KeyValuePair<string, Claim>(relationshipId, held));
        }
    }
}
