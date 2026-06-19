using System.Text.Json;
using vos.ManagedMicroservice.Shared.Contracts.Validation;

namespace vos.ManagedMicroservice.Metabolism.Models;

[ContractSchema("https://villageos/contracts/handle-request-metabolism.schema.json")]
public record HandleRequest(
    string? RelationshipId,
    string SubjectId,
    string TargetId,
    string? SubjectName,
    string? TargetName,
    JsonElement? Properties
);
