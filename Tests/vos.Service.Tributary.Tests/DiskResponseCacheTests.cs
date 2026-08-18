using FluentAssertions;
using vos.Service.Tributary.Services;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Unit tests for DiskResponseCache — the on-disk store behind the DiskCache kind (#5918).
// Files carry real extensions derived from the media type so a cached tile opens in any
// viewer; unknown types fall back to .bin plus a sidecar carrying the exact Content-Type.
public class DiskResponseCacheTests : IDisposable
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0xFF, 0xFE, 0x00, 0x01 };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vos-cache-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // A settable clock so expiry is tested by moving time, not by sleeping.
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly TestClock _clock = new();

    private DiskResponseCache Cache() => new(_root, _clock);

    [Fact]
    public void CacheKey_SameAddressAndAccept_IsStable()
    {
        var a = DiskResponseCache.CacheKey(new Uri("https://tiles.test/tile/1/0/1"), "image/webp");
        var b = DiskResponseCache.CacheKey(new Uri("https://tiles.test/tile/1/0/1"), "image/webp");

        a.Should().Be(b);
        a.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void CacheKey_DiffersByAddressAndByAccept()
    {
        var baseline = DiskResponseCache.CacheKey(new Uri("https://tiles.test/tile/1/0/1"), "image/webp");

        DiskResponseCache.CacheKey(new Uri("https://tiles.test/tile/1/0/2"), "image/webp")
            .Should().NotBe(baseline);
        DiskResponseCache.CacheKey(new Uri("https://tiles.test/tile/1/0/1"), "image/jpeg")
            .Should().NotBe(baseline);
        DiskResponseCache.CacheKey(new Uri("https://tiles.test/tile/1/0/1"), null)
            .Should().NotBe(baseline);
    }

    [Fact]
    public void WriteThenRead_RoundTripsBytesAndContentType_AsARealExtensionFile()
    {
        var cache = Cache();
        var key = DiskResponseCache.CacheKey(new Uri("https://tiles.test/t"), null);

        cache.Write("WorldImagery", key, PngBytes, "image/png");
        var hit = cache.TryRead("WorldImagery", key, TimeSpan.FromMinutes(5));

        hit.Should().NotBeNull();
        hit!.Value.Bytes.Should().Equal(PngBytes);
        hit.Value.ContentType.Should().Be("image/png");
        File.Exists(Path.Combine(_root, "WorldImagery", key + ".png")).Should().BeTrue();
    }

    [Fact]
    public void Write_MediaTypeWithParameters_MapsOnTheBareType()
    {
        var cache = Cache();
        var key = DiskResponseCache.CacheKey(new Uri("https://api.test/data"), null);

        cache.Write("EP", key, PngBytes, "image/jpeg; profile=x");
        var hit = cache.TryRead("EP", key, TimeSpan.FromMinutes(5));

        File.Exists(Path.Combine(_root, "EP", key + ".jpg")).Should().BeTrue();
        hit!.Value.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public void Write_UnknownContentType_FallsBackToBinWithSidecarCarryingTheExactType()
    {
        var cache = Cache();
        var key = DiskResponseCache.CacheKey(new Uri("https://api.test/lerc"), null);

        cache.Write("EP", key, PngBytes, "application/x-lerc");
        var hit = cache.TryRead("EP", key, TimeSpan.FromMinutes(5));

        File.Exists(Path.Combine(_root, "EP", key + ".bin")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "EP", key + ".meta.json")).Should().BeTrue();
        hit!.Value.ContentType.Should().Be("application/x-lerc");
    }

    [Fact]
    public void TryRead_PastTimeToLive_MissesAndOverwriteReplacesTheOldExtension()
    {
        var cache = Cache();
        var key = DiskResponseCache.CacheKey(new Uri("https://tiles.test/t"), null);
        cache.Write("EP", key, PngBytes, "image/png");

        _clock.Now = _clock.Now.AddMinutes(10);

        cache.TryRead("EP", key, TimeSpan.FromMinutes(5)).Should().BeNull();

        var fresher = new byte[] { 1, 2, 3 };
        cache.Write("EP", key, fresher, "image/webp");
        var hit = cache.TryRead("EP", key, TimeSpan.FromMinutes(5));

        hit!.Value.Bytes.Should().Equal(fresher);
        hit.Value.ContentType.Should().Be("image/webp");
        File.Exists(Path.Combine(_root, "EP", key + ".webp")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "EP", key + ".png")).Should().BeFalse("a rewrite must not leave a stale twin under the old extension");
    }

    [Fact]
    public void TryRead_UnknownKey_Misses()
    {
        Cache().TryRead("EP", new string('0', 64), TimeSpan.FromMinutes(5)).Should().BeNull();
    }

    [Fact]
    public void EndpointName_WithPathHostileCharacters_IsSanitizedIntoOneDirectory()
    {
        var cache = Cache();
        var key = DiskResponseCache.CacheKey(new Uri("https://api.test/x"), null);

        cache.Write(@"A/B:C..\D", key, PngBytes, "image/png");

        Directory.GetDirectories(_root).Should().HaveCount(1,
            "a hostile endpoint name must stay one directory under the root, not traverse out of it");
        cache.TryRead(@"A/B:C..\D", key, TimeSpan.FromMinutes(5)).Should().NotBeNull();
    }
}
