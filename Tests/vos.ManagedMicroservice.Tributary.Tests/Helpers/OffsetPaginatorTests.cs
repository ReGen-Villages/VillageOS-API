using System.Text.Json;
using FluentAssertions;
using vos.ManagedMicroservice.Tributary.Helpers;
using Xunit;

namespace vos.ManagedMicroservice.Tributary.Tests.Helpers;

// Unit tests for the source-agnostic offset paginator (Task #5470). The field names (has-more flag,
// items array, offset/size params) are all config, so an ArcGIS FeatureServer and a generic
// offset/limit list API are two configurations of the same loop. Delegate-driven — no HTTP.
public class OffsetPaginatorTests
{
    // ESRI FeatureServer shape.
    private static OffsetPaginationConfig EsriConfig(int? pageSize = 2) =>
        new("resultOffset", "resultRecordCount", pageSize, "exceededTransferLimit", "features");

    [Fact]
    public async Task FetchAllPagesAsync_SinglePageNotExceeded_FetchesOnce()
    {
        var offsets = new List<int>();
        Task<string> Fetch(int offset, CancellationToken ct)
        {
            offsets.Add(offset);
            return Task.FromResult(Page("features", new[] { 1, 2 }, "exceededTransferLimit", more: false));
        }

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, EsriConfig());

        offsets.Should().Equal(0);
        Ids(result, "features").Should().Equal(1, 2);
    }

    [Fact]
    public async Task FetchAllPagesAsync_MultiPage_AggregatesAllInOrder()
    {
        var offsets = new List<int>();
        Task<string> Fetch(int offset, CancellationToken ct)
        {
            offsets.Add(offset);
            return Task.FromResult(offset switch
            {
                0 => Page("features", new[] { 1, 2 }, "exceededTransferLimit", true),
                2 => Page("features", new[] { 3, 4 }, "exceededTransferLimit", true),
                4 => Page("features", new[] { 5 }, "exceededTransferLimit", false),
                _ => Page("features", Array.Empty<int>(), "exceededTransferLimit", false),
            });
        }

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, EsriConfig());

        offsets.Should().Equal(0, 2, 4);
        Ids(result, "features").Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task FetchAllPagesAsync_Aggregate_PreservesFirstPageFieldsAndClearsHasMore()
    {
        Task<string> Fetch(int offset, CancellationToken ct) => Task.FromResult(offset == 0
            ? Page("features", new[] { 1 }, "exceededTransferLimit", true)
            : Page("features", new[] { 2 }, "exceededTransferLimit", false));

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, EsriConfig(1));

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("objectIdFieldName").GetString().Should().Be("OBJECTID");
        doc.RootElement.GetProperty("exceededTransferLimit").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task FetchAllPagesAsync_NoPageSize_IncrementsByReturnedItemCount()
    {
        var offsets = new List<int>();
        Task<string> Fetch(int offset, CancellationToken ct)
        {
            offsets.Add(offset);
            return Task.FromResult(offset switch
            {
                0 => Page("features", new[] { 1, 2, 3 }, "exceededTransferLimit", true),
                3 => Page("features", new[] { 4, 5 }, "exceededTransferLimit", false),
                _ => Page("features", Array.Empty<int>(), "exceededTransferLimit", false),
            });
        }

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, EsriConfig(pageSize: null));

        offsets.Should().Equal(0, 3);
        Ids(result, "features").Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task FetchAllPagesAsync_HasMoreButEmptyPage_StopsToAvoidInfiniteLoop()
    {
        var offsets = new List<int>();
        Task<string> Fetch(int offset, CancellationToken ct)
        {
            offsets.Add(offset);
            return Task.FromResult(offset == 0
                ? Page("features", new[] { 1, 2 }, "exceededTransferLimit", true)
                : Page("features", Array.Empty<int>(), "exceededTransferLimit", true));
        }

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, EsriConfig());

        offsets.Should().Equal(0, 2);
        Ids(result, "features").Should().Equal(1, 2);
    }

    [Fact]
    public async Task FetchAllPagesAsync_FirstPageWithoutItemsArray_ReturnedVerbatim()
    {
        var calls = 0;
        Task<string> Fetch(int offset, CancellationToken ct)
        {
            calls++;
            return Task.FromResult("{\"error\":{\"code\":498,\"message\":\"Invalid token.\"}}");
        }

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, EsriConfig());

        calls.Should().Be(1);
        result.Should().Be("{\"error\":{\"code\":498,\"message\":\"Invalid token.\"}}");
    }

    [Fact]
    public async Task FetchAllPagesAsync_GenericFieldNames_AggregatesWithoutEsriVocabulary()
    {
        // A non-ESRI offset/limit list API: different param names, has-more field, and items array.
        var offsets = new List<int>();
        Task<string> Fetch(int offset, CancellationToken ct)
        {
            offsets.Add(offset);
            return Task.FromResult(offset switch
            {
                0 => Page("items", new[] { 1, 2 }, "hasMore", true),
                2 => Page("items", new[] { 3 }, "hasMore", false),
                _ => Page("items", Array.Empty<int>(), "hasMore", false),
            });
        }
        var config = new OffsetPaginationConfig("offset", "limit", 2, "hasMore", "items");

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, config);

        offsets.Should().Equal(0, 2);
        Ids(result, "items").Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task FetchAllPagesAsync_NestedItemsPath_AggregatesAtPath()
    {
        Task<string> Fetch(int offset, CancellationToken ct) => Task.FromResult(offset == 0
            ? Nested(new[] { 1, 2 }, more: true)
            : Nested(new[] { 3 }, more: false));
        var config = new OffsetPaginationConfig("offset", null, 2, "exceededTransferLimit", "data.features");

        var result = await OffsetPaginator.FetchAllPagesAsync(Fetch, config);

        using var doc = JsonDocument.Parse(result);
        var ids = doc.RootElement.GetProperty("data").GetProperty("features").EnumerateArray()
            .Select(f => f.GetProperty("attributes").GetProperty("OBJECTID").GetInt32()).ToArray();
        ids.Should().Equal(1, 2, 3);
    }

    // ---------- helpers ----------

    private static string Page(string itemsKey, int[] ids, string hasMoreKey, bool more) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["objectIdFieldName"] = "OBJECTID",
            [itemsKey] = ids.Select(id => new { attributes = new { OBJECTID = id } }).ToArray(),
            [hasMoreKey] = more,
        });

    private static string Nested(int[] ids, bool more) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["data"] = new Dictionary<string, object>
            {
                ["features"] = ids.Select(id => new { attributes = new { OBJECTID = id } }).ToArray(),
            },
            ["exceededTransferLimit"] = more,
        });

    private static int[] Ids(string json, string itemsKey)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(itemsKey).EnumerateArray()
            .Select(f => f.GetProperty("attributes").GetProperty("OBJECTID").GetInt32())
            .ToArray();
    }
}
