using System.Text.Json;
using System.Text.Json.Nodes;

namespace vos.ManagedMicroservice.Tributary.Helpers;

/// <summary>
/// How to walk an offset-paginated list endpoint: which query param advances the window, the optional
/// page-size param + value, the simple dotted path to the boolean "there is more" flag, and the dotted
/// path to the array to merge. Source-agnostic — an ArcGIS FeatureServer is just
/// <c>offsetParam=resultOffset</c>, <c>hasMorePath=exceededTransferLimit</c>, <c>itemsPath=features</c>.
/// </summary>
public sealed record OffsetPaginationConfig(
    string OffsetParam,
    string? PageSizeParam,
    int? PageSize,
    string HasMorePath,
    string ItemsPath);

/// <summary>
/// Drives an offset-paginated list endpoint across its pages (Task #5470) and concatenates every
/// page's items into the first page's body, so a downstream JSONata transform runs once over the
/// complete result. Loops while the page's <see cref="OffsetPaginationConfig.HasMorePath"/> flag is
/// true, advancing the offset by the page size (or by the returned item count when no size is set).
///
/// The per-page HTTP call is injected as <paramref name="fetchPage"/> (given the offset), so the loop,
/// offset arithmetic, and aggregation are independent of how a request is built/sent.
/// </summary>
public static class OffsetPaginator
{
    // Defensive ceiling: a well-behaved endpoint drops the has-more flag once drained. Hitting this
    // means it keeps claiming more while we advance — surface it rather than loop forever.
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

            // A response that isn't a paginated-list object can't be aggregated — return it verbatim.
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

        // Fully drained — the aggregate must not still advertise more.
        SetFalse(aggregate, config.HasMorePath);
        return aggregate.ToJsonString();
    }

    /// <summary>The array at a dotted path within <paramref name="root"/>, or null if absent/not an array.</summary>
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

    /// <summary>Set the boolean at a dotted path to false, if its parent object and the key exist.</summary>
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
