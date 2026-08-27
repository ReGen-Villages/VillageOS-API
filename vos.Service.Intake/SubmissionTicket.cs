using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace vos.Service.Intake;

/// <summary>
/// A short-lived value this service issues to a form and reads back when the form is posted. It is the
/// automated-submission check on the one route anybody may call.
/// </summary>
/// <remarks>
/// What it establishes: whoever posted answered a code sent to one particular address, recently. A post
/// arriving with nothing, with a value signed by somebody else, with one issued longer ago than a ticket
/// lasts, or with one issued for a different address than the submission names, never had that exchange.
/// <para>
/// The address is not in the ticket. What is in it is a mark this service computes from the address under
/// its own key, so a ticket in the open says nothing about whose mailbox it was issued against, and the
/// mark cannot be worked out without the key. <see cref="WhyRefused"/> asks whether the ticket is one of
/// ours and still live, which is the cheap question and is asked before the body is read;
/// <see cref="WasIssuedFor"/> asks whose it is, once the submission has been read and there is an address
/// to compare against.
/// </para>
/// <para>
/// The signing key is made when the process starts, so a ticket is only good at the instance that issued
/// it. One service answers one hostname, which is the deployment <c>deploy/Caddyfile</c> describes.
/// </para>
/// </remarks>
public sealed class SubmissionTicket(TimeProvider time)
{
    public const string HeaderName = "X-Submission-Ticket";

    /// <summary>Long enough to cover a form being posted after it was asked for, short enough that a ticket
    /// cannot be kept and used indefinitely.</summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromMinutes(10);

    private const int IssuedAtBytes = sizeof(long);
    private static readonly int MarkBytes = HMACSHA256.HashSizeInBytes;
    private static readonly int SignedBytes = IssuedAtBytes + MarkBytes;

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

    public string Issue(string emailAddress)
    {
        var signed = new byte[SignedBytes];
        BinaryPrimitives.WriteInt64BigEndian(signed, time.GetUtcNow().ToUnixTimeSeconds());
        MarkFor(emailAddress).CopyTo(signed.AsSpan(IssuedAtBytes));
        return Base64Url.EncodeToString([.. signed, .. HMACSHA256.HashData(_signingKey, signed)]);
    }

    /// <summary>Whether this ticket was issued against this address. A ticket that is not one of ours at
    /// all answers false, so a caller cannot tell a forged ticket from one issued to somebody else.
    /// </summary>
    public bool WasIssuedFor(string? presented, string emailAddress) =>
        TryReadSigned(presented, out var signed)
        && CryptographicOperations.FixedTimeEquals(
            signed.AsSpan(IssuedAtBytes), MarkFor(emailAddress));

    // Under this service's own key rather than a plain digest of the address: an address is drawn from a
    // small enough set that a bare digest of one could be recognised by trying candidates against it.
    private byte[] MarkFor(string emailAddress) =>
        HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(AddressVerification.Key(emailAddress)));

    /// <summary>Why the ticket was not accepted, or null when it was. The wording is what a person whose
    /// form sat open too long needs, because that is the only way this refuses a legitimate submission.
    /// </summary>
    public string? WhyRefused(string? presented)
    {
        const string askAgain = "Verify the address again and submit.";

        if (!TryReadSigned(presented, out var signed))
            return $"This submission did not carry a ticket this service issued. {askAgain}";

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(signed));
        return time.GetUtcNow() - issuedAt > ValidFor
            ? $"The form this was submitted from has been open too long. {askAgain}"
            : null;
    }

    private bool TryReadSigned(string? presented, out byte[] signed)
    {
        signed = [];
        if (string.IsNullOrWhiteSpace(presented)) return false;

        byte[] decoded;
        try { decoded = Base64Url.DecodeFromChars(presented); }
        catch (FormatException) { return false; }

        if (decoded.Length != SignedBytes + HMACSHA256.HashSizeInBytes) return false;

        var claimed = decoded.AsSpan(0, SignedBytes);
        if (!CryptographicOperations.FixedTimeEquals(
                decoded.AsSpan(SignedBytes), HMACSHA256.HashData(_signingKey, claimed)))
            return false;

        signed = claimed.ToArray();
        return true;
    }
}
