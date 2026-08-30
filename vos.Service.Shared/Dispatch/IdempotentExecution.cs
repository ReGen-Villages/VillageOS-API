using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("vos.Service.Shared.Tests")]

namespace vos.Service.Shared.Dispatch;

// Handled-predicate dispatch is at-least-once: the broker sends a relation again when it cannot
// confirm the handler finished, and a handler that reconnects is sent what it missed. Nothing in a
// delivery says whether it is the first.
//
// A claim is the note kept here against a relationshipId work has started for. A failing effect
// gives its claim back: keeping it would lose the work, because the re-drive would find it taken.
//
// None of this is a durable record of what completed. That belongs on the relation, which outlives
// any one process; these claims guard against a burst of repeats and nothing more.
public sealed class IdempotentExecution
{
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(1);

    private sealed class Claim(DateTimeOffset takenAt)
    {
        public DateTimeOffset TakenAt { get; } = takenAt;
    }

    private readonly ConcurrentDictionary<string, Claim> _claims = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    // A relationship never delivered again would keep its claim forever, so a sweep falls due once
    // every Retention. Judging a claim as it is asked for, rather than sweeping on every call, keeps
    // one delivery costing the same however many claims are held.
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
