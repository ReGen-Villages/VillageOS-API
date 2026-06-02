using System.Text;
using System.Text.Json;

namespace vos.ManagedMicroservice.Tributary.Helpers;

/// <summary>
/// Pure construction of the outbound HTTP request Tributary dispatches to a REST source, from the
/// endpoint thing's effective properties: custom headers, query-string parameters, request
/// content-type, and timeout (Task #5469). These are the base capabilities any REST source needs;
/// source-specific behavior (e.g. ESRI token auth and FeatureServer pagination) is layered on top
/// under Task #5470 and does not live here.
///
/// Extracted from <c>Program.cs</c>'s <c>CallEndpointAsync</c> so each capability is unit-testable in
/// isolation, mirroring the <see cref="EffectivePropertyResolver"/> extraction under Task #5436.
/// </summary>
public static class OutboundRequest
{
    /// <summary>Default request body content-type when <c>requestContentType</c> is not configured.</summary>
    public const string DefaultContentType = "application/json";

    /// <summary>Default outbound timeout when <c>timeout</c> is absent or invalid.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Methods that carry a request body. GET/DELETE/HEAD do not.</summary>
    public static bool MethodSupportsBody(string method) =>
        method is "POST" or "PUT" or "PATCH";

    /// <summary>
    /// Append <paramref name="queryParameters"/> to <paramref name="baseUri"/>, preserving any query
    /// already present in the URI. Null/empty leaves the URI unchanged.
    /// </summary>
    public static Uri ApplyQueryParameters(Uri baseUri, IReadOnlyDictionary<string, string>? queryParameters)
    {
        if (queryParameters == null || queryParameters.Count == 0)
            return baseUri;

        var builder = new UriBuilder(baseUri);
        var added = string.Join("&", queryParameters.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        // UriBuilder.Query round-trips with a leading '?'; concatenate onto any existing query.
        var existing = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existing) ? added : $"{existing}&{added}";
        return builder.Uri;
    }

    /// <summary>
    /// Interpret an effective <c>timeout</c> value (seconds, as a JSON number or numeric string) as a
    /// <see cref="TimeSpan"/>. Non-numeric, non-positive, or out-of-range values fall back to
    /// <see cref="DefaultTimeout"/>.
    /// </summary>
    public static TimeSpan ResolveTimeout(JsonElement value)
    {
        double seconds;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDouble(out var n):
                seconds = n;
                break;
            case JsonValueKind.String when double.TryParse(value.GetString(), out var s):
                seconds = s;
                break;
            default:
                return DefaultTimeout;
        }

        // >= the boundary, not >: TimeSpan.FromSeconds(TimeSpan.MaxValue.TotalSeconds) itself overflows.
        if (double.IsNaN(seconds) || seconds <= 0 || seconds >= TimeSpan.MaxValue.TotalSeconds)
            return DefaultTimeout;

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Parse a JSON object of string values (e.g. <c>headers</c>, <c>queryParams</c>) into a
    /// case-insensitive name/value map. Returns null if <paramref name="value"/> is not a JSON object;
    /// non-string member values are coerced to their raw text.
    /// </summary>
    public static Dictionary<string, string>? TryParseStringMap(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return null;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in value.EnumerateObject())
        {
            map[member.Name] = member.Value.ValueKind == JsonValueKind.String
                ? member.Value.GetString() ?? string.Empty
                : member.Value.GetRawText();
        }
        return map;
    }

    /// <summary>
    /// Build the outbound <see cref="HttpRequestMessage"/>: method + URI (with query params merged),
    /// optional JSON body for body-bearing methods using <paramref name="contentType"/>, and custom
    /// <paramref name="headers"/>.
    /// </summary>
    public static HttpRequestMessage Build(
        string method,
        Uri url,
        JsonElement body,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? queryParameters,
        string contentType)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), ApplyQueryParameters(url, queryParameters));

        if (MethodSupportsBody(method) && body.ValueKind != JsonValueKind.Undefined)
        {
            var json = JsonSerializer.Serialize(body);
            request.Content = new StringContent(json, Encoding.UTF8, contentType);
        }

        if (headers != null)
        {
            foreach (var (name, value) in headers)
            {
                // Request vs content headers are partitioned by HttpClient; TryAddWithoutValidation on
                // the request headers silently rejects content headers (e.g. Content-Type), so a stray
                // content header in the map is dropped rather than throwing.
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }
}
