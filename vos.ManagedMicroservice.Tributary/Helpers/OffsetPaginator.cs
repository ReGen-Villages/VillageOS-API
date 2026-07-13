using System.Text.Json;
using System.Text.Json.Nodes;

namespace vos.ManagedMicroservice.Tributary.Helpers;

public sealed record OffsetPaginationConfig(
    string OffsetParam,
    string? PageSizeParam,
    int? PageSize,
    string HasMorePath,
    string ItemsPath);

// Drives an offset-paginated list endpoint across its pages and concatenates every page's items into
// the first page's body, so a downstream JSONata transform runs once over the complete result.
public static class OffsetPaginator
{
    // Guard against an endpoint that keeps claiming more while we advance — surface it, don't loop forever.
    private const int MaxPages = 10_000;

    public static async Task<string> FetchAllPagesAsync(
        Func<int, CancellationToken, Task<string>> fetchPage,
        OffsetPaginationConfig config,
        CancellationToken cancellationToken = default)
    {
        var offset = 0;
        var pageNumber = 0;
        JsonObject? aggregate = null;
        JsonArray? aggregateItems = null;

        while (true)
        {
            if (pageNumber++ >= MaxPages)
                throw new InvalidOperationException(
                    $"Offset pagination exceeded {MaxPages} pages; aborting to avoid an unbounded loop.");

            var body = await fetchPage(offset, cancellationToken);

            JsonNode? node;
            try { node = JsonNode.Parse(body); }
            catch (JsonException) { node = null; }

            // Not a paginated-list object: can't be aggregated, return verbatim.
            if (node is not JsonObject page)
                return aggregate?.ToJsonString() ?? body;

            var items = NavigateArray(page, config.ItemsPath);
            var count = items?.Count ?? 0;

            if (aggregate == null)
            {
                if (items == null)
                    return body;

                aggregate = page;
                aggregateItems = items;
            }
            else if (items != null && aggregateItems != null)
            {
                // DeepClone detaches each item from its own page's tree before re-parenting.
                foreach (var item in items)
                    aggregateItems.Add(item?.DeepClone());
            }

            if (!IsMore(page, config.HasMorePath) || count == 0)
                break;

            offset += config.PageSize is > 0 ? config.PageSize.Value : count;
        }

        // The aggregate must not still advertise more.
        SetFalse(aggregate, config.HasMorePath);
        return aggregate.ToJsonString();
    }

    private static JsonArray? NavigateArray(JsonObject root, string dottedPath)
    {
        JsonNode? node = root;
        foreach (var segment in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node is not JsonObject obj)
                return null;
            node = obj[segment];
        }
        return node as JsonArray;
    }

    private static bool IsMore(JsonObject root, string dottedPath)
    {
        JsonNode? node = root;
        foreach (var segment in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node is not JsonObject obj)
                return false;
            node = obj[segment];
        }
        return node is JsonValue value && value.TryGetValue<bool>(out var more) && more;
    }

    private static void SetFalse(JsonObject root, string dottedPath)
    {
        var segments = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return;

        JsonNode? node = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (node is not JsonObject obj)
                return;
            node = obj[segments[i]];
        }

        if (node is JsonObject parent && parent[segments[^1]] is JsonValue)
            parent[segments[^1]] = false;
    }
}
