using vos.Service.Shared.Roles;

namespace vos.Service.Intake.Helpers;

// Identifiers derived from the submission rather than generated fresh. A wizard saves as it goes and a
// planner can double-click, so the same submission has to land on the same Things every time — the
// fragment endpoint keys its upsert on the identifier, and a fresh one would build a second site beside
// the first.
public static class StableIdentity
{
    // Never change this constant: every identifier already derived from a submission would shift.
    private static readonly Guid IntakeNamespace = new("3b7c1a94-6d52-5f18-9a4e-0c8b2d5e7f31");

    public static Guid Derive(string submissionId, string role) => DeriveVersion5($"{role}:{submissionId}");

    public static Guid DerivePredicate(string predicateName) => DeriveVersion5($"predicate:{predicateName}");

    private static Guid DeriveVersion5(string name) => StableIdentifier.DeriveVersion5(IntakeNamespace, name);
}
