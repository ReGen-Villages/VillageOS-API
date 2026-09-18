using System.Text.Json;
using FluentAssertions;
using vos.Service.Shared;
using vos.Service.Tributary.Helpers;
using vos.Service.Tributary.Services;
using Xunit;

namespace vos.Service.Tributary.Tests;

// Every check an outgoing call needs, asked on its own: none of these resolves a Thing, reads a
// kind, or sends anything. What each one costs is the settings it is handed and nothing else.
public class EndpointCallChecksTests
{
    private static readonly ResolvedKind Json = new("JsonResponse", []);
    private static readonly ResolvedKind Binary = new("BinaryResponse", []);
    private static readonly ResolvedKind TokenExchange = new("TokenExchangeAuth", ["tokenUrl", "tokenPath"]);
    private static readonly ResolvedKind OffsetPaging = new("OffsetPaging", []);
    private static readonly ResolvedKind DiskCache = new("DiskCache", ["cacheTtl"]);

    // ---------- what the kinds require ----------

    [Fact]
    public void UnmetRequirement_EverythingSupplied_RefusesNothing()
    {
        var kinds = new EndpointKinds(null, TokenExchange, null, null);

        EndpointCallChecks.UnmetRequirement(kinds, Effective(("tokenUrl", "https://t"), ("tokenPath", "token")))
            .Should().BeNull();
    }

    [Fact]
    public void UnmetRequirement_KeyLeftBlank_NamesTheKindAndTheKey()
    {
        var kinds = new EndpointKinds(null, TokenExchange, null, null);

        var refusal = EndpointCallChecks.UnmetRequirement(kinds, Effective(("tokenUrl", ""), ("tokenPath", "token")));

        refusal!.StatusCode.Should().Be(400);
        refusal.Error.Should().Contain("TokenExchangeAuth").And.Contain("tokenUrl");
    }

    // ---------- the response body ----------

    [Theory]
    [InlineData(null, false)]
    [InlineData("JsonResponse", false)]
    [InlineData("BinaryResponse", true)]
    public void TryBodyKind_KindThisServiceImplements_SaysWhetherTheBodyIsBytes(string? name, bool expectBinary)
    {
        var kind = name == null ? null : new ResolvedKind(name, []);

        EndpointCallChecks.TryBodyKind(kind, out var binary, out var refusal).Should().BeTrue();

        binary.Should().Be(expectBinary);
        refusal.Should().BeNull();
    }

    [Fact]
    public void TryBodyKind_KindNothingHereImplements_RefusesNamingBothHalves()
    {
        EndpointCallChecks.TryBodyKind(new ResolvedKind("XmlResponse", []), out _, out var refusal).Should().BeFalse();

        refusal!.Error.Should().Contain("XmlResponse").And.Contain("JsonResponse").And.Contain("BinaryResponse");
    }

    // ---------- the address ----------

    [Fact]
    public void TryAddress_UrlAndMethodPresent_ParsesTheOneAndNormalisesTheOther()
    {
        EndpointCallChecks.TryAddress(Effective(("url", "https://api.test/x"), ("httpMethod", " get ")), null,
            out var address, out var refusal).Should().BeTrue();

        address!.Uri.Should().Be(new Uri("https://api.test/x"));
        address.Method.Should().Be("GET");
        refusal.Should().BeNull();
    }

    [Fact]
    public void TryAddress_EitherKeyMissing_RefusesNamingBoth()
    {
        EndpointCallChecks.TryAddress(Effective(("url", "https://api.test/x")), null, out _, out var refusal)
            .Should().BeFalse();

        refusal!.StatusCode.Should().Be(400);
        refusal.Error.Should().Contain("url").And.Contain("httpMethod");
    }

    [Fact]
    public void TryAddress_KeyDeclaredTwice_RefusesCarryingEveryPathItMatched()
    {
        var effective = Effective(("a.url", "https://one"), ("b.url", "https://two"), ("httpMethod", "GET"));

        EndpointCallChecks.TryAddress(effective, null, out _, out var refusal).Should().BeFalse();

        refusal!.Error.Should().Contain("ambiguous");
        refusal.Detail!["conflicts"].Should().BeEquivalentTo(
            new Dictionary<string, List<string>?> { ["url"] = ["a.url", "b.url"], ["httpMethod"] = null });
    }

    [Fact]
    public void TryAddress_BlankUrl_Refuses()
    {
        EndpointCallChecks.TryAddress(Effective(("url", "  "), ("httpMethod", "GET")), null, out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("non-empty");
    }

    // Before the address is parsed, so an address still carrying a placeholder cannot become a Uri
    // that looks callable.
    [Fact]
    public void TryAddress_PlaceholderWithNoValue_RefusesBeforeParsing()
    {
        var effective = Effective(("url", "https://api.test/{site}/x"), ("httpMethod", "GET"));

        EndpointCallChecks.TryAddress(effective, new Dictionary<string, string>(), out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("site");
        refusal.Detail!["unfilledPlaceholders"].Should().BeEquivalentTo(new[] { "site" });
    }

    [Fact]
    public void TryAddress_PlaceholderFilled_ParsesTheFilledAddress()
    {
        var effective = Effective(("url", "https://api.test/{site}/x"), ("httpMethod", "GET"));

        EndpointCallChecks.TryAddress(effective, new Dictionary<string, string> { ["site"] = "alpha" },
            out var address, out _).Should().BeTrue();

        address!.Uri.AbsolutePath.Should().Be("/alpha/x");
    }

    [Fact]
    public void TryAddress_UrlThatIsNotAbsolute_Refuses()
    {
        EndpointCallChecks.TryAddress(Effective(("url", "not a url"), ("httpMethod", "GET")), null, out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("Invalid endpoint url");
    }

    [Fact]
    public void TryAddress_MethodNothingSupports_Refuses()
    {
        EndpointCallChecks.TryAddress(Effective(("url", "https://api.test/x"), ("httpMethod", "FETCH")), null, out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("Unsupported httpMethod").And.Contain("FETCH");
    }

    // ---------- the reshape ----------

    [Fact]
    public void TryReshape_NothingRegisteredNothingRequested_CompilesNothing()
    {
        EndpointCallChecks.TryReshape(Effective(), requested: null, binary: false, out var reshape, out var refusal)
            .Should().BeTrue();

        reshape!.Registered.Should().BeNull();
        reshape.Transform.Should().BeNull();
        refusal.Should().BeNull();
    }

    [Fact]
    public void TryReshape_RegisteredExpression_CompilesIt()
    {
        EndpointCallChecks.TryReshape(Effective(("responseTransform", "$.x")), null, false, out var reshape, out _)
            .Should().BeTrue();

        reshape!.Registered.Should().Be("$.x");
        reshape.Transform.Should().NotBeNull();
    }

    // A request-supplied expression wins for this call alone and is never written back, so what
    // the endpoint has registered is still reported as what it has registered.
    [Fact]
    public void TryReshape_RequestedExpression_WinsOverTheRegisteredOneWithoutReplacingIt()
    {
        EndpointCallChecks.TryReshape(Effective(("responseTransform", "$.x")), "$.y", false, out var reshape, out _)
            .Should().BeTrue();

        reshape!.Registered.Should().Be("$.x");
        reshape.Transform!.Eval("""{"x":1,"y":2}""").Should().Be("2");
    }

    [Fact]
    public void TryReshape_BinaryBodyWithAnExpression_RefusesBeforeCompiling()
    {
        EndpointCallChecks.TryReshape(Effective(("responseTransform", "this is not jsonata (")), null, true, out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("cannot be combined with a response transform");
    }

    [Fact]
    public void TryReshape_ExpressionThatDoesNotCompile_RefusesWithTheCompilerWords()
    {
        EndpointCallChecks.TryReshape(Effective(("responseTransform", "$.(")), null, false, out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("Invalid responseTransform");
        refusal.Detail!["detail"].Should().BeOfType<string>().Which.Should().NotBeEmpty();
    }

    [Fact]
    public void TryReshape_KeyDeclaredTwice_Refuses()
    {
        EndpointCallChecks.TryReshape(Effective(("a.responseTransform", "$.x"), ("b.responseTransform", "$.y")), null, false,
            out _, out var refusal).Should().BeFalse();

        refusal!.Error.Should().Contain("ambiguous").And.Contain("responseTransform");
    }

    // ---------- an optional setting ----------

    [Fact]
    public void TryOptionalText_Absent_IsNullAndNotARefusal()
    {
        EndpointCallChecks.TryOptionalText(Effective(), "acceptHeader", out var text, out var refusal).Should().BeTrue();

        text.Should().BeNull();
        refusal.Should().BeNull();
    }

    [Fact]
    public void TryOptionalText_DeclaredTwice_RefusesNamingTheKey()
    {
        EndpointCallChecks.TryOptionalText(Effective(("a.acceptHeader", "x"), ("b.acceptHeader", "y")), "acceptHeader",
            out _, out var refusal).Should().BeFalse();

        refusal!.Error.Should().Contain("acceptHeader");
        refusal.Detail!["conflicts"].Should().BeEquivalentTo(
            new Dictionary<string, List<string>> { ["acceptHeader"] = ["a.acceptHeader", "b.acceptHeader"] });
    }

    [Fact]
    public void TryOptionalMap_ObjectValue_ReadsItAsStringPairs()
    {
        EndpointCallChecks.TryOptionalMap(EffectiveRaw(("headers", """{"X-A":"1","X-B":"2"}""")), "headers",
            out var map, out _).Should().BeTrue();

        map.Should().BeEquivalentTo(new Dictionary<string, string> { ["X-A"] = "1", ["X-B"] = "2" });
    }

    [Fact]
    public void TryRequestContentType_Absent_IsTheDefault()
    {
        EndpointCallChecks.TryRequestContentType(Effective(), out var contentType, out _).Should().BeTrue();

        contentType.Should().Be(OutboundRequest.DefaultContentType);
    }

    [Fact]
    public void TryRequestContentType_Declared_IsWhatWasDeclared()
    {
        EndpointCallChecks.TryRequestContentType(Effective(("requestContentType", "text/plain")), out var contentType, out _)
            .Should().BeTrue();

        contentType.Should().Be("text/plain");
    }

    [Fact]
    public void TryTimeout_Declared_IsReadAsSeconds()
    {
        EndpointCallChecks.TryTimeout(Effective(("timeout", "5")), out var timeout, out _).Should().BeTrue();

        timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    // ---------- the credential ----------

    [Fact]
    public void TryCredential_NoKind_IsAPlainCall()
    {
        EndpointCallChecks.TryCredential(null, Effective(), out var credential, out var refusal).Should().BeTrue();

        credential.Should().BeNull();
        refusal.Should().BeNull();
    }

    [Fact]
    public void TryCredential_PreMintedToken_NeedsNoExchange()
    {
        var effective = Effective(("token", "PRE"), ("tokenParam", " key "), ("tokenHeader", "X-Auth"), ("tokenScheme", "Bearer"));

        EndpointCallChecks.TryCredential(TokenExchange, effective, out var credential, out _).Should().BeTrue();

        credential!.PreMinted.Should().Be("PRE");
        credential.Fetch.Should().BeNull();
        credential.Param.Should().Be("key");
        credential.Header.Should().Be("X-Auth");
        credential.Scheme.Should().Be("Bearer");
    }

    [Fact]
    public void TryCredential_ExchangeSettings_DescribeTheMintWithoutMakingIt()
    {
        var effective = EffectiveRaw(
            ("tokenUrl", "\"https://t/token\""),
            ("tokenRequest", """{"username":"u","password":"p"}"""),
            ("tokenPath", "\"access_token\""),
            ("expiryPath", "\"expires\""),
            ("expiryUnit", "\"epochMillis\""));

        EndpointCallChecks.TryCredential(TokenExchange, effective, out var credential, out _).Should().BeTrue();

        credential!.PreMinted.Should().BeNull();
        credential.Param.Should().Be("token", "the default when nothing names the parameter");
        credential.Fetch.Should().BeEquivalentTo(new TokenExchangeRequest(
            "https://t/token", new Dictionary<string, string> { ["username"] = "u", ["password"] = "p" },
            "access_token", "expires", "epochMillis"));
    }

    // Reached only when the kind declares fewer requirements than the mechanism needs; the kind's
    // own check is the one an endpoint author sees, this guards the mechanism against a kind that
    // under-declares.
    [Fact]
    public void TryCredential_KindThatUnderDeclares_RefusesSayingWhatTheMechanismNeeds()
    {
        var underDeclared = new ResolvedKind("TokenExchangeAuth", []);

        EndpointCallChecks.TryCredential(underDeclared, Effective(("tokenUrl", "https://t")), out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("tokenRequest").And.Contain("tokenPath");
    }

    [Fact]
    public void TryCredential_KindNothingHereImplements_Refuses()
    {
        EndpointCallChecks.TryCredential(new ResolvedKind("BasicAuth", []), Effective(), out _, out var refusal)
            .Should().BeFalse();

        refusal!.Error.Should().Contain("BasicAuth").And.Contain("TokenExchangeAuth");
    }

    // ---------- the pages ----------

    [Fact]
    public void TryPaging_NoKind_ReadsInOne()
    {
        EndpointCallChecks.TryPaging(null, Effective(), out var paging, out var refusal).Should().BeTrue();

        paging.Should().BeNull();
        refusal.Should().BeNull();
    }

    [Fact]
    public void TryPaging_OffsetSettings_DescribeTheWalk()
    {
        var effective = Effective(
            ("offsetParam", "offset"), ("pageSizeParam", " limit "), ("pageSize", "50"),
            ("hasMorePath", "$.more"), ("itemsPath", "$.items"));

        EndpointCallChecks.TryPaging(OffsetPaging, effective, out var paging, out _).Should().BeTrue();

        paging.Should().Be(new OffsetPaginationConfig("offset", "limit", 50, "$.more", "$.items"));
    }

    [Fact]
    public void TryPaging_PageSizeThatIsNotAPositiveNumber_IsLeftToTheProvider()
    {
        var effective = Effective(("offsetParam", "o"), ("pageSize", "lots"), ("hasMorePath", "m"), ("itemsPath", "i"));

        EndpointCallChecks.TryPaging(OffsetPaging, effective, out var paging, out _).Should().BeTrue();

        paging!.PageSize.Should().BeNull();
    }

    [Fact]
    public void TryPaging_KindThatUnderDeclares_Refuses()
    {
        EndpointCallChecks.TryPaging(OffsetPaging, Effective(("offsetParam", "o")), out _, out var refusal).Should().BeFalse();

        refusal!.Error.Should().Contain("hasMorePath").And.Contain("itemsPath");
    }

    [Fact]
    public void TryPaging_KindNothingHereImplements_Refuses()
    {
        EndpointCallChecks.TryPaging(new ResolvedKind("CursorPaging", []), Effective(), out _, out var refusal).Should().BeFalse();

        refusal!.Error.Should().Contain("CursorPaging").And.Contain("OffsetPaging");
    }

    // ---------- the cache ----------

    [Fact]
    public void TryCaching_NoKind_RefetchesEveryTime()
    {
        EndpointCallChecks.TryCaching(null, Effective(), out var cacheFor, out _).Should().BeTrue();

        cacheFor.Should().BeNull();
    }

    [Fact]
    public void TryCaching_DiskCacheWithATtl_ReadsItAsSeconds()
    {
        EndpointCallChecks.TryCaching(DiskCache, Effective(("cacheTtl", "90")), out var cacheFor, out _).Should().BeTrue();

        cacheFor.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("soon")]
    public void TryCaching_TtlThatIsNotAPositiveNumber_Refuses(string ttl)
    {
        EndpointCallChecks.TryCaching(DiskCache, Effective(("cacheTtl", ttl)), out _, out var refusal).Should().BeFalse();

        refusal!.Error.Should().Contain("cacheTtl");
    }

    // ---------- what cannot be combined ----------

    [Fact]
    public void PagingClash_BinaryBodyReadPageByPage_RefusesNamingBothKinds()
    {
        var kinds = new EndpointKinds(Binary, null, OffsetPaging, null);
        var paging = new OffsetPaginationConfig("o", null, null, "m", "i");

        var refusal = EndpointCallChecks.PagingClash(kinds, binary: true, paging);

        refusal!.Error.Should().Contain("BinaryResponse").And.Contain("OffsetPaging");
    }

    [Fact]
    public void PagingClash_TextBodyReadPageByPage_IsFine()
    {
        var kinds = new EndpointKinds(Json, null, OffsetPaging, null);
        var paging = new OffsetPaginationConfig("o", null, null, "m", "i");

        EndpointCallChecks.PagingClash(kinds, binary: false, paging).Should().BeNull();
    }

    [Fact]
    public void CachingClash_CachedResponseAssembledPageByPage_Refuses()
    {
        var kinds = new EndpointKinds(null, null, OffsetPaging, DiskCache);
        var paging = new OffsetPaginationConfig("o", null, null, "m", "i");

        EndpointCallChecks.CachingClash(kinds, TimeSpan.FromSeconds(1), paging, "GET", default)!
            .Error.Should().Contain("DiskCache").And.Contain("OffsetPaging");
    }

    // A credentialed response served from disk would answer a later call without its credential.
    [Fact]
    public void CachingClash_CachedResponseBehindACredential_Refuses()
    {
        var kinds = new EndpointKinds(null, TokenExchange, null, DiskCache);

        EndpointCallChecks.CachingClash(kinds, TimeSpan.FromSeconds(1), null, "GET", default)!
            .Error.Should().Contain("DiskCache").And.Contain("TokenExchangeAuth");
    }

    // The body is not part of the cache key, so two calls differing only in body would share an answer.
    [Fact]
    public void CachingClash_OutboundBodyOnAMethodThatCarriesOne_Refuses()
    {
        var kinds = new EndpointKinds(null, null, null, DiskCache);
        var body = JsonDocument.Parse("""{"q":1}""").RootElement;

        EndpointCallChecks.CachingClash(kinds, TimeSpan.FromSeconds(1), null, "POST", body)!
            .Error.Should().Contain("body");
    }

    [Fact]
    public void CachingClash_OutboundBodyOnAMethodThatDropsIt_IsFine()
    {
        var kinds = new EndpointKinds(null, null, null, DiskCache);
        var body = JsonDocument.Parse("""{"q":1}""").RootElement;

        EndpointCallChecks.CachingClash(kinds, TimeSpan.FromSeconds(1), null, "GET", body).Should().BeNull();
    }

    [Fact]
    public void CachingClash_NothingCached_IsFine()
    {
        var kinds = new EndpointKinds(null, TokenExchange, OffsetPaging, null);
        var paging = new OffsetPaginationConfig("o", null, null, "m", "i");

        EndpointCallChecks.CachingClash(kinds, null, paging, "POST", JsonDocument.Parse("{}").RootElement).Should().BeNull();
    }

    // ---------- harness ----------

    private static IReadOnlyDictionary<string, JsonElement> Effective(params (string Key, string Text)[] entries) =>
        EffectiveRaw([.. entries.Select(entry => (entry.Key, JsonSerializer.Serialize(entry.Text)))]);

    private static IReadOnlyDictionary<string, JsonElement> EffectiveRaw(params (string Key, string Json)[] entries) =>
        entries.ToDictionary(
            entry => entry.Key,
            entry => JsonDocument.Parse(entry.Json).RootElement,
            StringComparer.OrdinalIgnoreCase);
}
