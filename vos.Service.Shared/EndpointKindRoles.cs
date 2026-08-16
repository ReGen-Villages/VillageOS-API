namespace vos.Service.Shared;

// The roles an endpoint fills by reaching a kind Thing: how it authenticates, how it pages, how its
// body reads. Delta writes these edges and Tributary reads them, so the two must spell them
// identically — a role only one side spells right is an endpoint that reaches a kind nobody sees,
// and that reads as an endpoint with no kind at all rather than as an error. One definition, shared,
// is what makes that impossible rather than merely unlikely.
public static class EndpointKindRoles
{
    public const string Authentication = "authenticatesBy";
    public const string Paging = "pagesBy";
    public const string ResponseBody = "readsBodyAs";

    public static readonly IReadOnlyList<string> All = [Authentication, Paging, ResponseBody];

    // The property each role was written as before a kind became a Thing. A seed still carrying one
    // is refused at provisioning, because writing it would leave the endpoint reaching nothing while
    // looking configured.
    public static readonly IReadOnlyDictionary<string, string> SupersededProperties =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["authKind"] = Authentication,
            ["pagingKind"] = Paging,
            ["responseKind"] = ResponseBody,
        };

    public static bool IsRole(string predicateName) =>
        All.Contains(predicateName, StringComparer.OrdinalIgnoreCase);
}
