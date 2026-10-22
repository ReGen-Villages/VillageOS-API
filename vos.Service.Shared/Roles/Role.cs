using System.Security.Cryptography;
using System.Text;

namespace vos.Service.Shared.Roles;

// A role the platform reads a model through. The word appears only in what a refusal says; the model is read
// by the identifier.
public sealed record Role(string Word, Guid Id);

// A role is a Thing the model declares, and it binds the one Thing playing it along `binds`. Its identifier is
// the standard version 5 identifier of its word under one namespace, so every seed declares the same role
// and a model may call the Thing playing it whatever suits it.
public static class RoleIdentity
{
    public static readonly Guid Namespace = new("c33fcf16-6706-4f93-bfa5-8997bfaaf4bd");

    public static Guid Of(string word) => StableIdentifier.DeriveVersion5(Namespace, word);
}

// RFC 4122 version 5 (name-based, SHA-1), in canonical byte order, so a derivation done anywhere agrees
// with one done here.
public static class StableIdentifier
{
    public static Guid DeriveVersion5(Guid namespaceIdentifier, string name)
    {
        Span<byte> namespaceBytes = stackalloc byte[16];
        namespaceIdentifier.TryWriteBytes(namespaceBytes, bigEndian: true, out _);

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
