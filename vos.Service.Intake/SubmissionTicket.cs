using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace vos.Service.Intake;

/// <summary>
/// A short-lived value this service issues to a form and reads back when the form is posted. It is the
/// automated-submission check on the one route anybody may call.
/// </summary>
/// <remarks>
/// What it establishes: whoever posted asked this service for a ticket first, recently. A post arriving
/// with nothing, with a value signed by somebody else, or with one issued longer ago than a ticket lasts,
/// never had that exchange — which is every submitter that found the address and posted to it blind.
/// <para>
/// What it does not establish: that the caller is a person. A ticket is not tracked once issued and asking
/// for one costs nothing, so an automated submitter that fetches before each post satisfies it. What
/// bounds that submitter is the rate limit; the ticket is what makes it come and ask. A platform meant to
/// run without a third-party challenge service can claim this much and no more.
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

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

    public string Issue()
    {
        var issuedAt = new byte[IssuedAtBytes];
        BinaryPrimitives.WriteInt64BigEndian(issuedAt, time.GetUtcNow().ToUnixTimeSeconds());
        return Base64Url.EncodeToString([.. issuedAt, .. HMACSHA256.HashData(_signingKey, issuedAt)]);
    }

    /// <summary>Why the ticket was not accepted, or null when it was. The wording is what a person whose
    /// form sat open too long needs, because that is the only way this refuses a legitimate submission.
    /// </summary>
    public string? WhyRefused(string? presented)
    {
        const string askAgain = "Reload the form and submit again.";

        if (!TryReadIssuedAt(presented, out var issuedAt))
            return $"This submission did not carry a ticket this service issued. {askAgain}";

        return time.GetUtcNow() - issuedAt > ValidFor
            ? $"The form this was submitted from has been open too long. {askAgain}"
            : null;
    }

    private bool TryReadIssuedAt(string? presented, out DateTimeOffset issuedAt)
    {
        issuedAt = default;
        if (string.IsNullOrWhiteSpace(presented)) return false;

        byte[] decoded;
        try { decoded = Base64Url.DecodeFromChars(presented); }
        catch (FormatException) { return false; }

        if (decoded.Length != IssuedAtBytes + HMACSHA256.HashSizeInBytes) return false;

        var claimed = decoded.AsSpan(0, IssuedAtBytes);
        if (!CryptographicOperations.FixedTimeEquals(
                decoded.AsSpan(IssuedAtBytes), HMACSHA256.HashData(_signingKey, claimed)))
            return false;

        issuedAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(claimed));
        return true;
    }
}
