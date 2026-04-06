using System.Text.Json;

namespace vos.ManagedMicroservice.Metabolism.Models;

/// <summary>
/// Request payload for the /handle endpoint.
/// </summary>
public record HandleRequest(
    string? RelationshipId,
    string SubjectId,
    string TargetId,
    string? SubjectName,
    string? TargetName,
    JsonElement? Properties
);
