namespace vos.ManagedMicroservice.Shared;

/// <summary>
/// One sampled measurement for a property of an entity — the unit of the Observation write kind.
/// <paramref name="ObservedAt"/> is optional: omit it to let Mycelium stamp the arrival time, or set
/// it for late / out-of-order samples so current-value tracking uses observed-time, not arrival-time.
/// </summary>
public readonly record struct ObservationSample(string Property, object? Value, DateTime? ObservedAt = null);

/// <summary>
/// One historical reading for the bulk Sediment write kind. Unlike an <see cref="ObservationSample"/>,
/// every sediment reading names its entity (<paramref name="ThingId"/>) and must carry an
/// <paramref name="ObservedAt"/> — sediment is historical, never live.
/// </summary>
public readonly record struct SedimentReading(Guid ThingId, string Property, object? Value, DateTime ObservedAt);

/// <summary>Summary Mycelium returns for a Sediment deposit (<c>POST /api/sediment</c>).</summary>
public readonly record struct SedimentDepositResult(Guid BatchId, int Series, int Buckets, long Samples);
