using System.Security.Cryptography;
using System.Text;

namespace vos.Service.Intake.Helpers;

/// <summary>
/// Identifiers derived from the submission rather than generated fresh. A wizard saves as it goes and a
/// planner can double-click, so the same submission has to land on the same Things every time — the
/// fragment endpoint keys its upsert on the identifier, and a fresh one would build a second site beside
/// the first.
///
/// RFC 4122 version 5 (name-based, SHA-1), in canonical byte order, so a derivation done anywhere agrees
/// with one done here.
/// </summary>
public static class StableIdentity
{
    // Never change this constant: every identifier already derived from a submission would shift.
    private static readonly Guid IntakeNamespace = new("3b7c1a94-6d52-5f18-9a4e-0c8b2d5e7f31");

    public static Guid Derive(string submissionId, string role) => DeriveVersion5($"{role}:{submissionId}");

    public static Guid DerivePredicate(string predicateName) => DeriveVersion5($"predicate:{predicateName}");

    private static Guid DeriveVersion5(string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        IntakeNamespace.TryWriteBytes(namespaceBytes, bigEndian: true, out _);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        Span<byte> input = stackalloc byte[16 + nameBytes.Length];
        namespaceBytes.CopyTo(input);
        nameBytes.CopyTo(input[16..]);

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);

        Span<byte> identifier = hash[..16];
        identifier[6] = (byte)((identifier[6] & 0x0F) | 0x50);
        identifier[8] = (byte)((identifier[8] & 0x3F) | 0x80);
        return new Guid(identifier, bigEndian: true);
    }
}
