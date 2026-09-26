using Microsoft.Extensions.Configuration;
using vos.Service.Shared.Configuration;

namespace vos.Service.Feedback.Configuration;

// No allowed origin given is a relay only a page on its own host can call.
public sealed record FeedbackLaunchSettings(
    ServiceLaunchSettings Service,
    Uri DevOpsOrganization,
    string DevOpsAccessToken,
    string[] AllowedOrigins,
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
            return (null, "--devOpsOrganization must be the organisation's https address.\n\n" + UsageMessage);

        if (reader.ReadCredential(AccessTokenSetting) is not { Length: > 0 } accessToken)
            return (null, $"{AccessTokenSetting} must be set in configuration or the environment.\n\n" + UsageMessage);

        if (reader.Read("destinations") is not { Length: > 0 } destinationsPath)
            return (null, "--destinations must name the destinations file.\n\n" + UsageMessage);

        var (destinations, whyRefused) = Configuration.Destinations.Load(destinationsPath);
        if (destinations is null)
            return (null, whyRefused);

        var allowedOrigins = reader.Read("allowedOrigin")
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        return (new FeedbackLaunchSettings(service, organisation, accessToken, allowedOrigins, destinations), "");
    }

    public static string UsageMessage => ServiceLaunchSettings.BuildUsageMessage(
        " --devOpsOrganization=<address> --destinations=<file> [--allowedOrigin=<origin>[,<origin>]]",
        "\n  --devOpsOrganization  The Azure DevOps organisation's address, such as https://dev.azure.com/<name>"
        + "\n  --destinations        JSON file naming, per application, the project, areaPath, bugType, ideaType and tags"
        + "\n  --allowedOrigin       Origin(s) of pages allowed to call this service across origins"
        + $"\n\n  {AccessTokenSetting}     Personal access token with Work Items read and write, from configuration or the environment only");
}
