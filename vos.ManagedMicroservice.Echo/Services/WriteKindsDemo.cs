using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Echo.Services;

/// <summary>
/// A worked example of the three write kinds a microservice can use to write back to the model,
/// using the helpers inherited from <see cref="MyceliumClientBase"/>. Kept as a small, testable
/// service (rather than inline in <c>/handle</c>) so the demonstration is unit-tested and so authors
/// can copy it. See docs/MICROSERVICE_CONTRACT.md § "Writing data back".
///
/// Prerequisites for a live run: the target Thing exists and its properties accept the kind written —
/// <c>status</c> accepts Facts, <c>temperature</c>/<c>flow</c> accept Observations (AllowedWriteKinds).
/// </summary>
public sealed class WriteKindsDemo
{
    private readonly MyceliumClient _mycelium;

    public WriteKindsDemo(MyceliumClient mycelium) => _mycelium = mycelium;

    public async Task<WriteKindsDemoResult> RunAsync(Guid thingId, DateTime now)
    {
        // 1) FACT — structural truth, synchronous, never lossy. Returns the commit sequence number.
        var factSequence = await _mycelium.SetFactAsync(thingId, "status", "active");

        // 2) OBSERVATION (single) — one sampled telemetry value (202 Accepted, queued/batched).
        await _mycelium.RecordObservationAsync(thingId, "temperature", 21.5m, now);

        // 3) OBSERVATION (batch) — many samples across an entity's properties in one call.
        var batchAccepted = await _mycelium.RecordObservationsAsync(thingId, new[]
        {
            new ObservationSample("temperature", 21.7m, now),
            new ObservationSample("flow", 3.1m, now),
        });

        // 4) SEDIMENT — bulk historical readings written straight to sealed Sapwood (observedAt required).
        var deposit = await _mycelium.DepositSedimentAsync(new[]
        {
            new SedimentReading(thingId, "temperature", 19.8m, now.AddDays(-1)),
            new SedimentReading(thingId, "temperature", 20.4m, now.AddDays(-1).AddHours(1)),
        });

        return new WriteKindsDemoResult(
            FactSequence: factSequence,
            ObservationsAccepted: batchAccepted + 1, // batch + the single write above
            SedimentBatchId: deposit.BatchId,
            SedimentSamples: deposit.Samples);
    }
}

public readonly record struct WriteKindsDemoResult(
    long FactSequence, int ObservationsAccepted, Guid SedimentBatchId, long SedimentSamples);

/// <summary>Body for <c>POST /demo/write-kinds</c>: the (already-existing) Thing to write to.</summary>
public sealed record WriteKindsDemoRequest(Guid ThingId);
