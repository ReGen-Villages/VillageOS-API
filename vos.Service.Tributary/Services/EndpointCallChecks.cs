using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using vos.Service.Shared;
using vos.Service.Shared.Validation;
using vos.Service.Tributary.Helpers;

namespace vos.Service.Tributary.Services;

// The kind the endpoint reaches for each role, or null where it reaches none — which is a valid
// answer meaning "the plain behaviour": a plain body, no credential, no paging, no cache.
public sealed record EndpointKinds(
    ResolvedKind? Body, ResolvedKind? Authentication, ResolvedKind? Paging, ResolvedKind? Caching)
{
    public static EndpointKinds Reached(IReadOnlyDictionary<string, ResolvedKind> byRole) => new(
        byRole.GetValueOrDefault(EndpointKindRoles.ResponseBody),
        byRole.GetValueOrDefault(EndpointKindRoles.Authentication),
        byRole.GetValueOrDefault(EndpointKindRoles.Paging),
        byRole.GetValueOrDefault(EndpointKindRoles.Caching));
}

public sealed record EndpointAddress(Uri Uri, string Method);

// Registered is what the endpoint has in effect; Transform is what this call reshapes with, which a
// request may have supplied instead. Both are kept because the first is reported and the second run.
public sealed record Reshape(string? Registered, JsonataTransform? Transform);

// A credential is either already minted or described well enough to mint. Param, Header and Scheme
// say how it attaches to the call: a query parameter by default, or a header when one is named.
public sealed record Credential(
    string? PreMinted, TokenExchangeRequest? Fetch, string Param, string? Header, string? Scheme);

// Every check an outgoing call needs, each on its own: handed the endpoint's settings, it returns
// what it worked out or a refusal, and it neither reads the model nor sends anything. The mechanisms
// this service implements are named here by the kind Thing each answers to. The model owns which
// endpoints use which kind and what each requires; code owns only how the work is done, and a kind
// the model names and nothing here implements is refused saying both halves.
public static class EndpointCallChecks
{
    public const string TokenExchangeMechanism = "TokenExchangeAuth";
    public const string OffsetPagingMechanism = "OffsetPaging";
    public const string BinaryBodyMechanism = "BinaryResponse";
    public const string JsonBodyMechanism = "JsonResponse";
    public const string DiskCacheMechanism = "DiskCache";

    private const string BinaryTransformClash =
        "A binary body cannot be combined with a response transform: there is no text to transform.";

    // Refused before any outbound call, which is the whole reason a kind declares its requirements
    // instead of the code knowing them. The roles are asked in a fixed order so the first unmet one
    // is always the same one.
    public static Refusal? UnmetRequirement(EndpointKinds kinds, IReadOnlyDictionary<string, JsonElement> effective)
    {
        foreach (var kind in new[] { kinds.Body, kinds.Authentication, kinds.Paging, kinds.Caching })
        {
            var missing = EndpointKindResolver.MissingRequirements(kind, effective);
            if (missing.Count > 0)
                return Refusal.BadRequest(
                    $"Endpoint reaches kind '{kind!.Name}' but does not supply: {string.Join(", ", missing)}.");
        }
        return null;
    }

    // A byte-level read wrapped in a base64 envelope. What presupposes a decodable string body —
    // a transform, offset paging — is refused by the check that knows about it.
    public static bool TryBodyKind(ResolvedKind? kind, out bool binary, [NotNullWhen(false)] out Refusal? refusal)
    {
        binary = false;
        refusal = null;
        switch (kind?.Name)
        {
            case null:
            case JsonBodyMechanism:
                return true;
            case BinaryBodyMechanism:
                binary = true;
                return true;
            default:
                refusal = UnimplementedKind(EndpointKindRoles.ResponseBody, kind.Name, JsonBodyMechanism, BinaryBodyMechanism);
                return false;
        }
    }

    public static bool TryAddress(
        IReadOnlyDictionary<string, JsonElement> effective,
        IReadOnlyDictionary<string, string>? addressParameters,
        [NotNullWhen(true)] out EndpointAddress? address,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        address = null;
        List<string>? urlConflicts = null;
        List<string>? methodConflicts = null;

        if (!EffectivePropertyResolver.TryGetEffectiveProperty(effective, "url", out var urlElement, out urlConflicts)
            || !EffectivePropertyResolver.TryGetEffectiveProperty(effective, "httpMethod", out var methodElement, out methodConflicts))
        {
            refusal = urlConflicts != null || methodConflicts != null
                ? new Refusal(400, "Endpoint thing has ambiguous properties for url/httpMethod.", new Dictionary<string, object?>
                {
                    ["conflicts"] = new Dictionary<string, List<string>?> { ["url"] = urlConflicts, ["httpMethod"] = methodConflicts },
                })
                : Refusal.BadRequest("Endpoint thing is missing required properties: url, httpMethod");
            return false;
        }

        var url = Text(urlElement);
        var method = Text(methodElement);
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(method))
        {
            refusal = Refusal.BadRequest("Endpoint url/httpMethod must be non-empty strings.");
            return false;
        }

        // Before the address is parsed, so an address still carrying a placeholder cannot become a
        // Uri that looks callable.
        url = AddressPlaceholders.Fill(url!, addressParameters, out var unfilledPlaceholders);
        if (unfilledPlaceholders.Count > 0)
        {
            refusal = new Refusal(400,
                "Endpoint url has placeholders with no value in addressParameters: "
                + $"{string.Join(", ", unfilledPlaceholders)}.",
                new Dictionary<string, object?> { ["unfilledPlaceholders"] = unfilledPlaceholders });
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            refusal = Refusal.BadRequest($"Invalid endpoint url: {url}");
            return false;
        }

        var normalizedMethod = method!.Trim().ToUpperInvariant();
        if (!HttpMethodValidator.IsSupportedMethod(normalizedMethod))
        {
            refusal = Refusal.BadRequest($"Unsupported httpMethod: {method}");
            return false;
        }

        address = new EndpointAddress(uri, normalizedMethod);
        refusal = null;
        return true;
    }

    // What the endpoint reshapes with on this call. A request-supplied expression wins for this call
    // alone and is never written back: Tributary reads a source, and a read that rewrote its own
    // registration would change what every later caller of that source receives. Compiled here,
    // before the outbound call, so an expression that cannot parse costs the source nothing,
    // whichever side supplied it.
    public static bool TryReshape(
        IReadOnlyDictionary<string, JsonElement> effective,
        string? requested,
        bool binary,
        [NotNullWhen(true)] out Reshape? reshape,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        reshape = null;
        string? registered = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "responseTransform", out var element, out var conflicts))
            registered = Text(element);
        if (conflicts != null)
        {
            refusal = AmbiguousProperty("responseTransform", conflicts);
            return false;
        }

        var expression = string.IsNullOrWhiteSpace(requested) ? registered : requested;

        if (binary && !string.IsNullOrWhiteSpace(expression))
        {
            refusal = Refusal.BadRequest(BinaryTransformClash);
            return false;
        }

        JsonataTransform? transform = null;
        if (!string.IsNullOrWhiteSpace(expression))
        {
            try
            {
                transform = new JsonataTransform(expression!);
            }
            catch (Exception ex)
            {
                refusal = new Refusal(400, "Invalid responseTransform JSONata expression.",
                    new Dictionary<string, object?> { ["detail"] = ex.Message });
                return false;
            }
        }

        reshape = new Reshape(registered, transform);
        refusal = null;
        return true;
    }

    // Absent is an answer — null, and not a refusal. Only a key the model declares twice is refused.
    public static bool TryOptionalText(
        IReadOnlyDictionary<string, JsonElement> effective, string name,
        out string? text, [NotNullWhen(false)] out Refusal? refusal)
    {
        text = null;
        refusal = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, name, out var element, out var conflicts))
            text = Text(element);
        if (conflicts != null)
        {
            refusal = AmbiguousProperty(name, conflicts);
            return false;
        }
        return true;
    }

    public static bool TryOptionalMap(
        IReadOnlyDictionary<string, JsonElement> effective, string name,
        out Dictionary<string, string>? map, [NotNullWhen(false)] out Refusal? refusal)
    {
        map = null;
        refusal = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, name, out var element, out var conflicts))
            map = OutboundRequest.TryParseStringMap(element);
        if (conflicts != null)
        {
            refusal = AmbiguousProperty(name, conflicts);
            return false;
        }
        return true;
    }

    public static bool TryRequestContentType(
        IReadOnlyDictionary<string, JsonElement> effective,
        out string contentType, [NotNullWhen(false)] out Refusal? refusal)
    {
        contentType = OutboundRequest.DefaultContentType;
        if (!TryOptionalText(effective, "requestContentType", out var declared, out refusal))
            return false;
        if (!string.IsNullOrWhiteSpace(declared))
            contentType = declared!;
        return true;
    }

    public static bool TryTimeout(
        IReadOnlyDictionary<string, JsonElement> effective,
        out TimeSpan timeout, [NotNullWhen(false)] out Refusal? refusal)
    {
        timeout = OutboundRequest.DefaultTimeout;
        refusal = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "timeout", out var element, out var conflicts))
            timeout = OutboundRequest.ResolveTimeout(element);
        if (conflicts != null)
        {
            refusal = AmbiguousProperty("timeout", conflicts);
            return false;
        }
        return true;
    }

    // Reaching no kind is a plain call. TokenExchangeAuth uses a pre-minted token, or one minted from
    // a configured credential exchange — token endpoint, form fields, and response token/expiry paths
    // all come from the template, so nothing here is source-specific. The mint itself is a network
    // call and is not made here: it is deferred to the call, so its failures become 502 rather than 400.
    public static bool TryCredential(
        ResolvedKind? kind,
        IReadOnlyDictionary<string, JsonElement> effective,
        out Credential? credential,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        credential = null;
        refusal = null;
        switch (kind?.Name)
        {
            case null:
                return true;
            case TokenExchangeMechanism:
                break;
            default:
                refusal = UnimplementedKind(EndpointKindRoles.Authentication, kind.Name, TokenExchangeMechanism);
                return false;
        }

        var param = "token";
        if (!TryOptionalText(effective, "tokenParam", out var declaredParam, out refusal))
            return false;
        if (!string.IsNullOrWhiteSpace(declaredParam))
            param = declaredParam!.Trim();
        if (!TryOptionalText(effective, "tokenHeader", out var header, out refusal))
            return false;
        if (!TryOptionalText(effective, "tokenScheme", out var scheme, out refusal))
            return false;

        if (!TryOptionalText(effective, "token", out var preMinted, out refusal))
            return false;
        if (!string.IsNullOrWhiteSpace(preMinted))
        {
            credential = new Credential(preMinted, null, param, header, scheme);
            return true;
        }

        if (!TryOptionalText(effective, "tokenUrl", out var tokenUrl, out refusal))
            return false;
        if (!TryOptionalMap(effective, "tokenRequest", out var tokenRequest, out refusal))
            return false;
        if (!TryOptionalText(effective, "tokenPath", out var tokenPath, out refusal))
            return false;
        if (!TryOptionalText(effective, "expiryPath", out var expiryPath, out refusal))
            return false;
        if (!TryOptionalText(effective, "expiryUnit", out var expiryUnit, out refusal))
            return false;
        if (string.IsNullOrWhiteSpace(tokenUrl) || tokenRequest == null || tokenRequest.Count == 0 || string.IsNullOrWhiteSpace(tokenPath))
        {
            // Reached only when the kind declares fewer requirements than the mechanism needs. The
            // kind's own check is the one an endpoint author sees; this guards the mechanism against
            // a kind that under-declares.
            refusal = Refusal.BadRequest("Token exchange needs tokenUrl, tokenRequest and tokenPath, or a pre-minted token.");
            return false;
        }

        credential = new Credential(null,
            new TokenExchangeRequest(tokenUrl!, tokenRequest, tokenPath!, expiryPath, expiryUnit),
            param, header, scheme);
        return true;
    }

    public static bool TryPaging(
        ResolvedKind? kind,
        IReadOnlyDictionary<string, JsonElement> effective,
        out OffsetPaginationConfig? paging,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        paging = null;
        refusal = null;
        switch (kind?.Name)
        {
            case null:
                return true;
            case OffsetPagingMechanism:
                break;
            default:
                refusal = UnimplementedKind(EndpointKindRoles.Paging, kind.Name, OffsetPagingMechanism);
                return false;
        }

        if (!TryOptionalText(effective, "offsetParam", out var offsetParam, out refusal))
            return false;
        if (!TryOptionalText(effective, "pageSizeParam", out var pageSizeParam, out refusal))
            return false;
        if (!TryOptionalText(effective, "hasMorePath", out var hasMorePath, out refusal))
            return false;
        if (!TryOptionalText(effective, "itemsPath", out var itemsPath, out refusal))
            return false;
        if (string.IsNullOrWhiteSpace(offsetParam) || string.IsNullOrWhiteSpace(hasMorePath) || string.IsNullOrWhiteSpace(itemsPath))
        {
            refusal = Refusal.BadRequest("Offset paging needs offsetParam, hasMorePath and itemsPath.");
            return false;
        }

        int? pageSize = null;
        if (EffectivePropertyResolver.TryGetEffectiveProperty(effective, "pageSize", out var pageSizeElement, out var pageSizeConflicts))
        {
            var raw = pageSizeElement.ValueKind == JsonValueKind.String ? pageSizeElement.GetString() : pageSizeElement.GetRawText();
            if (!string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                pageSize = parsed;
        }
        if (pageSizeConflicts != null)
        {
            refusal = AmbiguousProperty("pageSize", pageSizeConflicts);
            return false;
        }

        paging = new OffsetPaginationConfig(
            offsetParam!,
            string.IsNullOrWhiteSpace(pageSizeParam) ? null : pageSizeParam!.Trim(),
            pageSize,
            hasMorePath!,
            itemsPath!);
        return true;
    }

    // Reaching no kind means every call refetches. DiskCache serves a repeated fetch from local disk
    // within cacheTtl; the combinations it cannot answer honestly are CachingClash's to refuse.
    public static bool TryCaching(
        ResolvedKind? kind,
        IReadOnlyDictionary<string, JsonElement> effective,
        out TimeSpan? cacheFor,
        [NotNullWhen(false)] out Refusal? refusal)
    {
        cacheFor = null;
        refusal = null;
        switch (kind?.Name)
        {
            case null:
                return true;
            case DiskCacheMechanism:
                break;
            default:
                refusal = UnimplementedKind(EndpointKindRoles.Caching, kind.Name, DiskCacheMechanism);
                return false;
        }

        if (!TryOptionalText(effective, "cacheTtl", out var rawTtl, out refusal))
            return false;
        if (!double.TryParse(rawTtl, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            refusal = Refusal.BadRequest("cacheTtl must be a positive number of seconds.");
            return false;
        }

        cacheFor = TimeSpan.FromSeconds(seconds);
        return true;
    }

    // A page is decoded text, and bytes cannot be.
    public static Refusal? PagingClash(EndpointKinds kinds, bool binary, OffsetPaginationConfig? paging) =>
        binary && paging != null
            ? Refusal.BadRequest($"A '{kinds.Body!.Name}' body cannot be read page by page as '{kinds.Paging!.Name}'.")
            : null;

    // What a cached answer cannot honestly stand in for, refused before anything is fetched or
    // written. A credentialed response served from disk would answer a later call without its
    // credential; refused until a keying design justifies otherwise. The body is not part of the
    // cache key, so a call that sends one cannot be told from a call that sent a different one.
    public static Refusal? CachingClash(
        EndpointKinds kinds, TimeSpan? cacheFor, OffsetPaginationConfig? paging, string method, JsonElement body)
    {
        if (cacheFor == null)
            return null;
        if (paging != null)
            return Refusal.BadRequest(
                $"A '{kinds.Caching!.Name}' response cannot be assembled page by page as '{kinds.Paging!.Name}'.");
        if (kinds.Authentication != null)
            return Refusal.BadRequest(
                $"A '{kinds.Caching!.Name}' response cannot be combined with '{kinds.Authentication.Name}'.");
        if (OutboundRequest.MethodSupportsBody(method) && body.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            return Refusal.BadRequest(
                "A request with an outbound body cannot be served from disk: the body is not part of the cache key.");
        return null;
    }

    // Says what the model asked for and what this service can actually do, because either half alone
    // sends the reader to the wrong place.
    private static Refusal UnimplementedKind(string role, string named, params string[] implemented) =>
        Refusal.BadRequest(
            $"The endpoint's '{role}' kind is '{named}', which this service does not implement. "
            + $"It implements: {string.Join(", ", implemented)}.");

    private static Refusal AmbiguousProperty(string name, List<string> conflicts) =>
        new(400, $"Endpoint thing has ambiguous properties for {name}.", new Dictionary<string, object?>
        {
            ["conflicts"] = new Dictionary<string, List<string>> { [name] = conflicts },
        });

    private static string? Text(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
}
