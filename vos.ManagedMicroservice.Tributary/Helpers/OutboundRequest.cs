using System.Text;
using System.Text.Json;

namespace vos.ManagedMicroservice.Tributary.Helpers;

public static class OutboundRequest
{
    public const string DefaultContentType = "application/json";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static bool MethodSupportsBody(string method) =>
        method is "POST" or "PUT" or "PATCH";

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
