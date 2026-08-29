using System.Globalization;
using System.Text.Json;
using System.Text;

namespace vos.Service.Tributary.Helpers;

public static class OutboundRequest
{
    public const string DefaultContentType = "application/json";

    // Providers refuse a caller that does not name itself: a request carrying no User-Agent is
    // answered 403 by protection layers that never see the address, which reads to a run as the
    // source being unreachable. A registration naming its own User-Agent still wins.
    public const string DefaultUserAgent = "VillageOS-Tributary";

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
            case JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s):
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
        string contentType,
        string? acceptHeader = null)
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

        if (!request.Headers.Contains("User-Agent"))
            request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);

        // The dedicated key wins over any Accept the generic headers map set — one value on the wire.
        // TryAddWithoutValidation keeps q-value lists ("image/tiff, image/png;q=0.8") verbatim.
        if (!string.IsNullOrWhiteSpace(acceptHeader))
        {
            request.Headers.Remove("Accept");
            request.Headers.TryAddWithoutValidation("Accept", acceptHeader.Trim());
        }

        return request;
    }
}
