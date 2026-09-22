using vos.Service.Shared.Configuration;

namespace vos.Service.Intake;

// Where this service hands a verification code to be delivered. A deployment supplies it; there is no
// default, because a code that goes nowhere is a route nobody can submit through.
//
// Host: The mail server that relays for this deployment.
// Port: Its submission port.
// From: The address a code is sent from, which is also where a reply lands.
// Username: The account to relay under, where the server asks for one.
// Password: Read from configuration alone, never from the command line, which is visible
// to every process on the host.
public sealed record MailSettings(string Host, int Port, string From, string? Username, string? Password)
{
    public const int SubmissionPort = 587;

    // The settings, or null where one that has no sensible default is missing. The caller says
    // what is missing and stops: a service that started without these would answer every verification
    // with a failure, and the first anybody would know of it is a submitter unable to submit.
    public static MailSettings? Parse(string[] arguments, IConfiguration configuration)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        var host = reader.Read("mailHost");
        var from = reader.Read("mailFrom");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return null;

        // A port given as something that is not one is a mistake, not a request for the default. Falling
        // back would start the service against a port nobody chose and say nothing about it.
        var port = SubmissionPort;
        if (reader.Read("mailPort") is { } named && !int.TryParse(named, out port)) return null;

        return new MailSettings(host, port, from, reader.Read("mailUser"), reader.ReadCredential("mailPassword"));
    }

    // Says where mail goes and never what it goes under. A record prints every property it
    // holds, so anything that ever writes these down would otherwise write the relay password down with
    // them.
    public override string ToString() =>
        $"{Host}:{Port} from {From} as {Username ?? "no account"}";

    public const string UsageSummary = " --mailHost=<host> --mailFrom=<address>";

    public static string UsageMessage =>
        "\n  --mailHost      Mail server that relays verification codes"
        + "\n  --mailPort      Its submission port (default " + SubmissionPort + ")"
        + "\n  --mailFrom      Address a verification code is sent from"
        + "\n  --mailUser      Account to relay under, where the server asks for one"
        + "\n  --mailPassword  That account's password — configuration or the environment only";
}
