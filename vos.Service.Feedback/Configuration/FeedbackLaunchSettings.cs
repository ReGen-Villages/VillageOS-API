using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Feedback.Configuration;

// No allowed origin given is a relay only a page on its own host can call.
public sealed record FeedbackLaunchSettings(
    ServiceLaunchSettings Service,
    Uri DevOpsOrganization,
    string DevOpsAccessToken,
    string[] AllowedOrigins,
    string? PathPrefix,
    Destinations Destinations)
{
    public const string AccessTokenSetting = "DevOpsAccessToken";

    public static (FeedbackLaunchSettings? Settings, string WhyRefused) Parse(string[]? arguments, IConfiguration? configuration = null)
    {
        var reader = new LaunchSettingReader(arguments, configuration);

        if (ServiceLaunchSettings.Parse(reader) is not { } service)
            return (null, UsageMessage);

        // The access token travels with every call, so only a stand-in on this machine is reached over plain http.
        if (!Uri.TryCreate(reader.Read("devOpsOrganization")?.TrimEnd('/'), UriKind.Absolute, out var organisation)
            || !(organisation.Scheme == Uri.UriSchemeHttps || (organisation.Scheme == Uri.UriSchemeHttp && organisation.IsLoopback)))
            return (null, "DevOpsOrganization must be the organisation's https address.\n\n" + UsageMessage);

        if (reader.ReadCredential(AccessTokenSetting) is not { Length: > 0 } accessToken)
            return (null, $"{AccessTokenSetting} must be set in configuration or the environment.\n\n" + UsageMessage);

        var (destinations, whyRefused) = Configuration.Destinations.From(configuration);
        if (destinations is null)
            return (null, whyRefused + "\n\n" + UsageMessage);

        var allowedOrigins = reader.Read("allowedOrigin")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        var pathPrefix = reader.Read("pathPrefix");
        if (pathPrefix is not null && !pathPrefix.StartsWith('/'))
            return (null, "--pathPrefix must begin with a slash, such as /feedback.\n\n" + UsageMessage);

        return (new FeedbackLaunchSettings(service, organisation, accessToken, allowedOrigins, pathPrefix, destinations), "");
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " [--allowedOrigin=<origin>[,<origin>]] [--pathPrefix=<prefix>]",
        "\n  --allowedOrigin  Origin(s) of pages allowed to call this service across origins"
        + "\n  --pathPrefix     Path prefix a proxy in front leaves on each request, such as /feedback, for one that cannot take it off"
        + "\n\nWhere reports are filed is read from appsettings.json, or from the one in the folder --contentRoot names:"
        + "\n  DevOpsOrganization  The Azure DevOps organisation's address, such as https://dev.azure.com/<name>"
        + "\n  Destinations        Per application: Project, AreaPath, BugType, IdeaType and Tags"
        + $"\n\n  {AccessTokenSetting}   Personal access token with Work Items read and write, from configuration or the environment only");
}
