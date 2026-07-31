namespace vos.Service.Shared;

// One sampled measurement; ObservedAt is optional (omit to let Mycelium stamp arrival time).
public readonly record struct ObservationSample(string Property, object? Value, DateTime? ObservedAt = null);

// One historical reading for a Sediment deposit; ObservedAt is required.
public readonly record struct SedimentReading(Guid ObjectId, string Property, object? Value, DateTime ObservedAt);

// Summary Mycelium returns for a Sediment deposit.
public readonly record struct SedimentDepositResult(Guid BatchId, int Series, int Buckets, long Samples);
