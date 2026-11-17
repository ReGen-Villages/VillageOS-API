using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Intake.Services;

// What the model declares for a public request, held between requests. Held for a short while rather than
// until a change is heard, because a stream of the model's changes goes quiet while the broker replaces its
// model, which is the one change that matters most. Nothing naming one submission belongs here: a read
// naming a contact carries the address a ticket is checked against.
public sealed class HeldDeclarations(IntakeMyceliumClient mycelium, TimeProvider clock)
{
    public static readonly TimeSpan HeldFor = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions AsTheBrokerReadsIt = new(JsonSerializerDefaults.Web);

    private readonly object _held = new();
    private Guid _model;
    private readonly Dictionary<string, Held> _byQuestion = new(StringComparer.Ordinal);

    private sealed record Held(DateTimeOffset AskedAt, Task<SnapshotDocument> Reading);

    public async Task<T> ReadAsync<T>(
        SubscriptionSelector selector, Func<SnapshotDocument, T> read, CancellationToken cancellation) =>
        read(await SnapshotAsync(selector, cancellation));

    private async Task<SnapshotDocument> SnapshotAsync(SubscriptionSelector selector, CancellationToken cancellation)
    {
        var model = ModelScopedBearer.Read(await mycelium.GetTokenAsync())?.ModelId ?? Guid.Empty;
        var question = JsonSerializer.Serialize(selector, AsTheBrokerReadsIt);
        var now = clock.GetUtcNow();

        Task<SnapshotDocument> reading;
        lock (_held)
        {
            if (model != _model)
            {
                _byQuestion.Clear();
                _model = model;
            }

            if (_byQuestion.TryGetValue(question, out var held) && now - held.AskedAt < HeldFor)
                reading = held.Reading;
            else
            {
                reading = mycelium.ReadSnapshotAsync(selector, CancellationToken.None);
                var asked = new Held(now, reading);
                _byQuestion[question] = asked;
                _ = reading.ContinueWith(
                    _ => Forget(question, asked),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        // Waited for rather than cancelled: the read is shared with every other request asking the same
        // question, and the broker's own timeout bounds it.
        return await reading.WaitAsync(cancellation);
    }

    private void Forget(string question, Held failed)
    {
        lock (_held)
        {
            if (_byQuestion.TryGetValue(question, out var held) && ReferenceEquals(held, failed))
                _byQuestion.Remove(question);
        }
    }
}
