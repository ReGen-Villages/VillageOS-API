namespace vos.Service.Metabolism.Models;

public record SimulationConfig(
    string RelationshipId,
    string SubjectId,
    string TargetId,
    string SubjectName,
    decimal Quantity,
    string Unit,
    string PropertyPath,
    decimal FrequencySeconds,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal StartDelaySeconds = 0m
)
{
    // Each tick makes two writes, so this holds a simulation to two writes a second.
    public const decimal ShortestFrequencySeconds = 1m;

    public TimeSpan Interval => TimeSpan.FromSeconds((double)FrequencySeconds);

    public static string? RefusalOfFrequency(decimal frequencySeconds) =>
        frequencySeconds < ShortestFrequencySeconds
            ? $"frequencySeconds must be at least {ShortestFrequencySeconds} second; it was {frequencySeconds}"
            : null;
}
