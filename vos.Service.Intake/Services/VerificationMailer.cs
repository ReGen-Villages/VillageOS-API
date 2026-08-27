using System.Net;
using System.Net.Mail;

namespace vos.Service.Intake.Services;

/// <summary>How a verification code reaches the address it was made for.</summary>
/// <remarks>One method, because that is the whole of what this service asks of mail. A deployment whose
/// server wants something this cannot do — a token exchange rather than a password, say — is one
/// implementation of this, and nothing else here changes.</remarks>
public interface IVerificationMailer
{
    Task SendAsync(string emailAddress, string code, CancellationToken cancellation);
}

/// <summary>
/// Hands the code to the deployment's own mail server.
/// </summary>
/// <remarks>
/// The message says what the code is for and how long it lasts, and carries nothing else about the
/// submission — not the site, not the project. Whoever reads it may be the wrong person, and what a
/// verification proves is only that somebody reads this mailbox.
/// <para>
/// This uses the mail client the base class library ships rather than taking a package for it. What is
/// sent is one short message to one recipient over an authenticated connection, which that client does;
/// the interface above is what a deployment needing more would replace.
/// </para>
/// </remarks>
public sealed class SmtpVerificationMailer(MailSettings settings) : IVerificationMailer
{
    /// <summary>What the code arrives as. Separate from the sending because this is the part worth
    /// checking: what a message carries is a decision, and handing it to a server is not.</summary>
    public static MailMessage MessageFor(MailSettings settings, string emailAddress, string code) =>
        new(settings.From, emailAddress)
        {
            Subject = "Your land submission code",
            Body = $"Your code is {code}.\n\n"
                   + $"It lasts {(int)AddressVerification.ValidFor.TotalMinutes} minutes. Enter it on the "
                   + "form to send your submission.\n\n"
                   + "If you did not ask to submit land, nothing has been submitted and you can ignore "
                   + "this message.",
        };

    /// <summary>What to relay under, or null where the server asks for nothing. Separate because relaying
    /// anonymously when a deployment configured an account is a failure it would only see in its mail
    /// server's log.</summary>
    public static NetworkCredential? CredentialFor(MailSettings settings) =>
        string.IsNullOrWhiteSpace(settings.Username)
            ? null
            : new NetworkCredential(settings.Username, settings.Password);

    public async Task SendAsync(string emailAddress, string code, CancellationToken cancellation)
    {
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true,
            Credentials = CredentialFor(settings),
        };
        using var message = MessageFor(settings, emailAddress, code);

        await client.SendMailAsync(message, cancellation);
    }
}
