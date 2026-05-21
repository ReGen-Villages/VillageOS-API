namespace vos.ManagedMicroservice.Shared.Validation;

/// <summary>
/// Validates supported HTTP methods.
/// </summary>
public static class HttpMethodValidator
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD",
        "POST",
        "PUT",
        "DELETE",
        "CONNECT",
        "OPTIONS",
        "TRACE",
        "PATCH"
    };

    public static bool IsSupportedMethod(string method) => SupportedMethods.Contains(method);
}
