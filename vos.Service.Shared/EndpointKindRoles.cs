namespace vos.Service.Shared;

// The roles an endpoint fills by reaching a kind Thing: how it authenticates, how it pages, how its
// body reads, how its responses are kept. A seed writes these relationships and Tributary reads them by
// name, so a role spelled differently in a seed reads as an endpoint with no kind at all rather than as an error.
public static class EndpointKindRoles
{
    public const string Authentication = "authenticatesBy";
    public const string Paging = "pagesBy";
    public const string ResponseBody = "readsBodyAs";
    public const string Caching = "cachesBy";
    public const string Keeping = "keepsBy";

    public static readonly IReadOnlyList<string> All = [Authentication, Paging, ResponseBody, Caching, Keeping];

    public static bool IsRole(string predicateName) =>
        All.Contains(predicateName, StringComparer.Ordinal);
}
