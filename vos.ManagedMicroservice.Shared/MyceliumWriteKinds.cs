namespace vos.ManagedMicroservice.Shared;

/// <summary>One sampled measurement; <paramref name="ObservedAt"/> is optional (omit to let Mycelium stamp arrival time).</summary>
public readonly record struct ObservationSample(string Property, object? Value, DateTime? ObservedAt = null);

/// <summary>One historical reading for a Sediment deposit; <paramref name="ObservedAt"/> is required.</summary>
public readonly record struct SedimentReading(Guid ThingId, string Property, object? Value, DateTime ObservedAt);

/// <summary>Summary Mycelium returns for a Sediment deposit.</summary>
public readonly record struct SedimentDepositResult(Guid BatchId, int Series, int Buckets, long Samples);
