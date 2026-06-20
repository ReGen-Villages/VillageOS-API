using vos.ManagedMicroservice.Shared;

namespace vos.ManagedMicroservice.Echo.Services;

/// <summary>Worked example of the three write kinds (docs/MICROSERVICE_CONTRACT.md § "Writing data
/// back"). A live run needs the Thing to exist with <c>status</c> Fact-writable and
/// <c>temperature</c>/<c>flow</c> Observation-writable.</summary>
public sealed class WriteKindsDemo
{
    private readonly MyceliumClient _mycelium;

    public WriteKindsDemo(MyceliumClient mycelium) => _mycelium = mycelium;

    public async Task<WriteKindsDemoResult> RunAsync(Guid thingId, DateTime now)
    {
        var factSequence = await _mycelium.SetFactAsync(thingId, "status", "active");
        await _mycelium.RecordObservationAsync(thingId, "temperature", 21.5m, now);
        var batchAccepted = await _mycelium.RecordObservationsAsync(thingId, new[]
        {
            new ObservationSample("temperature", 21.7m, now),
            new ObservationSample("flow", 3.1m, now),
        });
        var deposit = await _mycelium.DepositSedimentAsync(new[]
        {
            new SedimentReading(thingId, "temperature", 19.8m, now.AddDays(-1)),
            new SedimentReading(thingId, "temperature", 20.4m, now.AddDays(-1).AddHours(1)),
        });

        return new WriteKindsDemoResult(factSequence, batchAccepted + 1, deposit.BatchId, deposit.Samples);
    }
}

public readonly record struct WriteKindsDemoResult(
    long FactSequence, int ObservationsAccepted, Guid SedimentBatchId, long SedimentSamples);

public sealed record WriteKindsDemoRequest(Guid ThingId);
