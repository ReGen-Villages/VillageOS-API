namespace vos.Service.Shared;

/// <summary>What the broker says about the model's clock: the instant it has reached, and how fast it
/// runs against real time. A rate of one is a model on the wall clock.</summary>
public sealed record ModelTimeReading(DateTimeOffset Now, double Rate);
