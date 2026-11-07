using System.Globalization;
using System.Text.Json;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Phloem.Model;

// A failed snapshot is answered as a failure until one replaces it, so a look fails the way a failed read did and
// the keeper backs off as before.
public sealed class HeldCadences
{
    private readonly object _lock = new();
    private Dictionary<Guid, Cadence>? _held;
    private Exception? _failure;
    private TaskCompletionSource _firstSnapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Replace(IEnumerable<Cadence> cadences)
    {
        lock (_lock)
        {
            _held = cadences.ToDictionary(cadence => cadence.Id);
            _failure = null;
            _firstSnapshot.TrySetResult();
        }
    }

    public void NoteFailure(Exception failure)
    {
        lock (_lock)
        {
            if (_held is not null) return;
            _failure = failure;
            var waiting = _firstSnapshot;
            _firstSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting.TrySetException(failure);
        }
    }

    public async Task<IReadOnlyList<Cadence>> CurrentAsync(CancellationToken cancellationToken)
    {
        Task firstSnapshot;
        lock (_lock)
        {
            if (_held is not null) return _held.Values.ToList();
            if (_failure is { } failure) throw failure;
            firstSnapshot = _firstSnapshot.Task;
        }
        await firstSnapshot.WaitAsync(cancellationToken);
        lock (_lock) return _held?.Values.ToList() ?? [];
    }

    public void MarkDue(Guid cadenceId, DateTimeOffset due)
    {
        lock (_lock)
            if (_held is not null && _held.TryGetValue(cadenceId, out var cadence))
                _held[cadenceId] = cadence with { LastDue = due };
    }

    public void Apply(ModelChangeEvent change)
    {
        lock (_lock)
        {
            if (_held is null) return;
            switch (change.Kind)
            {
                case "ThingEntered" or "ThingCreated" when change.Thing is { IsArchetype: false } entered:
                    _held[entered.Id] = CadenceOf(entered);
                    break;
                case "ThingLeft" or "ThingDeleted":
                    _held.Remove(change.EntityId);
                    break;
                case "PropertyChanged" or "PropertyDeleted" when change.PropertyName is { } property && _held.TryGetValue(change.EntityId, out var cadence):
                    var value = change.Kind == "PropertyChanged" ? change.Value : null;
                    if (string.Equals(property, ModelNames.IntervalMinutes, StringComparison.OrdinalIgnoreCase))
                        _held[cadence.Id] = cadence with { IntervalMinutes = NumberOf(value) };
                    else if (string.Equals(property, ModelNames.LastDue, StringComparison.OrdinalIgnoreCase))
                        _held[cadence.Id] = cadence with { LastDue = InstantOf(value) };
                    break;
            }
        }
    }

    public static Cadence CadenceOf(SnapshotThing thing)
    {
        var values = thing.ValuesStated().ToDictionary(value => value.Key, value => value.Value.Value, StringComparer.OrdinalIgnoreCase);
        return new Cadence(thing.Id, thing.Name ?? string.Empty,
            values.TryGetValue(ModelNames.IntervalMinutes, out var interval) ? NumberOf(interval) : 0,
            values.TryGetValue(ModelNames.LastDue, out var lastDue) ? InstantOf(lastDue) : null);
    }

    private static double NumberOf(JsonElement? value) => value is { } given ? given.ValueKind switch
    {
        JsonValueKind.Number => given.GetDouble(),
        JsonValueKind.String when double.TryParse(given.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => 0,
    } : 0;

    private static DateTimeOffset? InstantOf(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } given
        && DateTimeOffset.TryParse(given.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant
            : null;
}
