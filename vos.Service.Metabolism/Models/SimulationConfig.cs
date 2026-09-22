namespace vos.Service.Metabolism.Models;

public record SimulationConfig(
    string RelationshipId,
    string SubjectId,
    string TargetId,
    string SubjectName,
    decimal Quantity,
    string Unit,
    string PropertyPath,
    int FrequencySeconds,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal StartDelaySeconds = 0m
);
