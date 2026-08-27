using System.Security.Cryptography;
using System.Text;

namespace vos.Service.Intake;

/// <summary>
/// The codes this service has sent to addresses, and what it will accept back.
/// </summary>
/// <remarks>
/// What it establishes: whoever answered with the code can read mail sent to that address. That is the
/// whole of it — it says nothing about who they are, and a person can hold as many addresses as they
/// like.
/// <para>
/// Anybody may ask for a code, so every bound here is a bound on what a stranger can make this service
/// do. A code dies after a fixed period and after a fixed number of wrong answers, so guessing it is
/// bounded rather than merely unlikely. One address may be sent only so many codes in a window, which is
/// what keeps the route from being aimed at somebody else's mailbox. What is held is capped, because
/// what a stranger can add to must not be able to grow without end.
/// </para>
/// <para>
/// Nothing here survives a restart. A code is good for minutes, and the person whose was lost asks for
/// another; keeping them would mean writing addresses to disk, which is the one thing this flow is meant
/// to avoid.
/// </para>
/// </remarks>
public sealed class AddressVerification(TimeProvider time)
{
    /// <summary>Long enough to find the message and read it, short enough that a code left lying in a
    /// mailbox is not a way in.</summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromMinutes(15);

    /// <summary>A six-figure code guessed at random comes up once in a million, so a handful of answers
    /// leaves guessing hopeless while a person who mistypes theirs is not locked out.</summary>
    public const int AnswersAllowed = 5;

    /// <summary>How many codes one address may be sent before it is left alone. Somebody asking again
    /// because the first did not arrive is ordinary; a dozen is this service being used to post to
    /// a mailbox its owner never gave us.</summary>
    public const int CodesPerAddress = 3;

    public static readonly TimeSpan CodeBudgetWindow = TimeSpan.FromHours(1);

    /// <summary>Beyond this the oldest is dropped, so the room a stranger can take is fixed. Somebody
    /// whose pending code is dropped asks for another.</summary>
    public const int MostAddressesHeld = 10_000;

    private const int CodeDigits = 6;

    // Derived rather than written out beside the digits, because two numbers that have to agree are two
    // numbers that can stop agreeing.
    private static readonly int CodesPossible = (int)Math.Pow(10, CodeDigits);

    private readonly Dictionary<string, Pending> _pending = [];
    private readonly Lock _gate = new();

    /// <summary>A code to send to this address, or null where it has been sent as many as the window
    /// allows. Asking again replaces whatever code was outstanding, so the last one sent is the one that
    /// works.</summary>
    public string? CodeFor(string emailAddress)
    {
        var key = Key(emailAddress);
        var now = time.GetUtcNow();

        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var held) || now - held.BudgetOpenedAt >= CodeBudgetWindow)
            {
                held = new Pending { BudgetOpenedAt = now };
                MakeRoom(now);
                _pending[key] = held;
            }

            if (held.CodesSent >= CodesPerAddress) return null;

            held.CodesSent++;
            held.Code = FreshCode();
            held.CodeExpiresAt = now + ValidFor;
            held.AnswersLeft = AnswersAllowed;
            return held.Code;
        }
    }

    /// <summary>
    /// The one thing a refused code is ever told, and the sameness is the point rather than an economy.
    /// Answering a code costs a caller nothing and they name the address themselves, so a message that
    /// distinguished a wrong code from an address nothing was sent to would answer "has somebody just
    /// started a submission under this address" for any address anybody cared to type.
    /// </summary>
    public const string NotTheCode =
        "That code is not one this service is waiting for. Check it, or ask for a new one.";

    /// <summary>Why the code was not accepted, or null when it was — in which case it is spent, and a
    /// ticket is what the caller carries from here on.</summary>
    public string? WhyRefused(string emailAddress, string? answered)
    {
        var key = Key(emailAddress);
        var now = time.GetUtcNow();

        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var held) || held.Code is null || now >= held.CodeExpiresAt)
                return NotTheCode;

            if (held.AnswersLeft <= 0) return NotTheCode;

            held.AnswersLeft--;
            if (!Matches(held.Code, answered)) return NotTheCode;

            held.Code = null;
            return null;
        }
    }

    /// <summary>Takes back a code that never left this service, so a mail server having a bad afternoon
    /// does not spend an address's budget on codes nobody could read and lock its owner out for the
    /// hour.</summary>
    public void NothingWasSent(string emailAddress)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(Key(emailAddress), out var held)) return;

            held.Code = null;
            if (held.CodesSent > 0) held.CodesSent--;
        }
    }

    // Case and surrounding space are not part of an address anybody meant, and a code sent to one spelling
    // has to be answerable by the other — otherwise a person who verified `Ana@example.pt` and submitted
    // `ana@example.pt` would be turned away by the ticket check with nothing to correct.
    public static string Key(string emailAddress) => emailAddress.Trim().ToLowerInvariant();

    private static string FreshCode() =>
        RandomNumberGenerator.GetInt32(0, CodesPossible).ToString($"D{CodeDigits}");

    // Fixed-time, because a comparison that stops at the first wrong figure tells a caller how much of the
    // code they had right.
    private static bool Matches(string held, string? answered) =>
        answered is not null
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(held), Encoding.UTF8.GetBytes(answered.Trim()));

    // Only when the room has run out, never on the way past. Sweeping everything held on each request
    // would make one caller's verification cost a walk of every address any caller had named, and nothing
    // depends on the sweep: an expired code is refused by its own expiry and a spent budget by its window.
    // So this reclaims, and what it does not reclaim is bounded by the cap.
    private void MakeRoom(DateTimeOffset now)
    {
        if (_pending.Count < MostAddressesHeld) return;

        // A record is kept while either half of it still applies: the code somebody is about to answer, or
        // the budget that stops the address being sent more.
        foreach (var (key, held) in _pending.ToArray())
        {
            if (now >= held.CodeExpiresAt && now - held.BudgetOpenedAt >= CodeBudgetWindow)
                _pending.Remove(key);
        }

        while (_pending.Count >= MostAddressesHeld)
            _pending.Remove(_pending.MinBy(held => held.Value.BudgetOpenedAt).Key);
    }

    private sealed class Pending
    {
        public string? Code;
        public DateTimeOffset CodeExpiresAt;
        public int AnswersLeft;
        public int CodesSent;
        public DateTimeOffset BudgetOpenedAt;
    }
}
