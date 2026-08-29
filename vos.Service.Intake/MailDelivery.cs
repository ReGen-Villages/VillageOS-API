using vos.Service.Shared.Configuration;

namespace vos.Service.Intake;

/// <summary>
/// How a verification code leaves this service.
/// </summary>
/// <remarks>
/// A deployment hands it to a mail server. A developer has no relay to hand — and on a domain whose policy
/// forbids application passwords cannot obtain one at all — so they may have the code written where they
/// can read it instead, which is the only way to run the exchange the submission route rests on without
/// one.
/// <para>
/// That second way verifies nobody: a code written down was received by nobody, so the route would accept
/// a submission naming any address at all. It is refused anywhere but a development machine, and
/// <see cref="WhyRefusedIn"/> is where that is decided.
/// </para>
/// </remarks>
/// <param name="Server">The server to relay through, or null where the code is written rather than sent.</param>
public sealed record MailDelivery(MailSettings? Server)
{
    public const string ToAServer = "server";
    public const string ToTheConsole = "console";

    /// <summary>The one environment a code nobody received is allowed in.</summary>
    private const string DevelopmentMachine = "Development";

    public bool IsToTheConsole => Server is null;

    /// <summary>The settings, or null where they say nothing this service can do. The caller says what is
    /// missing and stops.</summary>
    public static MailDelivery? Parse(string[] arguments, IConfiguration configuration)
    {
        var asked = new LaunchSettingReader(arguments, configuration).Read("mailDelivery") ?? ToAServer;

        // Named, because a record's copy constructor makes a bare null ambiguous with it.
        if (string.Equals(asked, ToTheConsole, StringComparison.OrdinalIgnoreCase))
            return new MailDelivery(Server: null);

        // A way of delivering this service does not have is a mistake, not a request for the default.
        // Falling back would start it sending through a server the operator did not ask for, and a typo
        // would read as working until somebody waited for mail.
        if (!string.Equals(asked, ToAServer, StringComparison.OrdinalIgnoreCase))
            return null;

        return MailSettings.Parse(arguments, configuration) is { } server ? new MailDelivery(server) : null;
    }

    /// <summary>Why this service may not start delivering this way here, or null where it may.</summary>
    public string? WhyRefusedIn(string environmentName) =>
        IsToTheConsole && !string.Equals(environmentName, DevelopmentMachine, StringComparison.OrdinalIgnoreCase)
            ? $"--mailDelivery={ToTheConsole} writes each code where whoever runs this service can read it "
              + "rather than sending it, so nobody has to receive one and no address is verified. It is for "
              + $"a developer with no relay to hand, and this service is running as '{environmentName}'."
            : null;

    public static string UsageMessage =>
        $"\n  --mailDelivery  Where a code goes: '{ToAServer}' (the default) or '{ToTheConsole}', which writes"
        + "\n                  it to the log instead of sending it and is refused outside Development"
        + MailSettings.UsageMessage;
}
