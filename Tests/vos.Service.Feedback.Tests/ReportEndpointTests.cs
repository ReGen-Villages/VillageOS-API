using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using vos.Auth.Shared;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Feedback.Tests;

public class ReportEndpointTests
{
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static string PersonToken(string name = "ada", string role = "Viewer") => TestTokens.Jwt(new Dictionary<string, object>
    {
        [ClaimTypes.Name] = name,
        [ClaimTypes.Role] = role,
        [VosClaims.TokenType] = "user",
        [VosClaims.ModelId] = FeedbackWebApplicationFactory.ModelId.ToString(),
    });

    private static string ServiceToken(string service = "kiosk-gateway") => TestTokens.Jwt(new Dictionary<string, object>
    {
        [ClaimTypes.Name] = $"service:{service}",
        [ClaimTypes.Role] = "Service",
        [VosClaims.TokenType] = "service",
        [VosClaims.ModelId] = FeedbackWebApplicationFactory.ModelId.ToString(),
    });

    private static string StreamToken() => TestTokens.Jwt(new Dictionary<string, object>
    {
        [ClaimTypes.Name] = "ada",
        [ClaimTypes.Role] = "Viewer",
        [VosClaims.TokenType] = "stream",
        [VosClaims.ModelId] = FeedbackWebApplicationFactory.ModelId.ToString(),
    });

    private static JsonObject Report(Action<JsonObject>? change = null)
    {
        var report = new JsonObject
        {
            ["application"] = "Trellis",
            ["kind"] = "bug",
            ["title"] = "The map stays blank after switching model",
            ["description"] = "Switched to the second model.\nThe map never drew.",
            ["context"] = new JsonObject
            {
                ["pageAddress"] = "https://app.example.org/operations",
                ["browser"] = "Firefox 131 on macOS",
                ["screenSize"] = "1440 × 900",
                ["language"] = "de",
            },
        };
        change?.Invoke(report);
        return report;
    }

    private static string DataAddress(string mediaType, byte[] bytes) => $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

    private static async Task<HttpResponseMessage> Post(
        FeedbackWebApplicationFactory factory, JsonObject report, string? token, string? origin = null)
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/reports")
        {
            Content = new StringContent(report.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin != null)
            request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static JsonArray Patch(FeedbackWebApplicationFactory factory) => JsonNode.Parse(factory.WorkItemCreation.Text)!.AsArray();

    private static string? Field(JsonArray patch, string field) =>
        patch.SingleOrDefault(operation => (string?)operation!["path"] == $"/fields/{field}")?["value"]?.GetValue<string>();

    private static FeedbackWebApplicationFactory RelayAccepting(params string[] tokens)
    {
        var factory = new FeedbackWebApplicationFactory();
        factory.AcceptedTokens.UnionWith(tokens);
        return factory;
    }

    [Fact]
    public async Task AReportCarryingNoToken_IsRefusedAndNothingReachesDevOps()
    {
        await using var factory = RelayAccepting();

        var response = await Post(factory, Report(), token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Body(response)).GetProperty("code").GetString().Should().Be("signInRequired");
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ATokenThePlatformRefuses_IsRefusedAndNothingReachesDevOps()
    {
        await using var factory = RelayAccepting();

        var response = await Post(factory, Report(), PersonToken());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.PlatformRequests.Should().ContainSingle().Which.Path.Should().Be("/api/models");
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEventStreamToken_IsRefusedEvenThoughThePlatformAcceptsIt()
    {
        var token = StreamToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(), token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ABugFromASignedInPerson_IsFiledAsTheDestinationsBugTypeInItsProjectAndArea()
    {
        var token = PersonToken("ada", "Viewer");
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Body(response)).GetProperty("reference").GetInt32().Should().Be(FeedbackWebApplicationFactory.CreatedWorkItem);

        var creation = factory.WorkItemCreation;
        creation.Address.ToString().Should().StartWith($"{FeedbackWebApplicationFactory.Organisation}/Clients/_apis/wit/workitems/$Bug");
        creation.ContentType.Should().Be("application/json-patch+json");
        creation.Authorization.Should().Be(
            "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes($":{FeedbackWebApplicationFactory.AccessToken}")));

        var patch = Patch(factory);
        Field(patch, "System.Title").Should().Be("The map stays blank after switching model");
        Field(patch, "System.AreaPath").Should().Be(@"Clients\Console");
        Field(patch, "System.Tags").Should().Be("Reported in app; Trellis; Console");
        Field(patch, "System.Description").Should().BeNull();

        var body = Field(patch, "Microsoft.VSTS.TCM.ReproSteps")!;
        body.Should().Contain("Switched to the second model.<br>The map never drew.");
        body.Should().Contain("ada").And.Contain("Viewer");
        body.Should().Contain(FeedbackWebApplicationFactory.ModelName);
        body.Should().Contain("https://app.example.org/operations");
        body.Should().Contain("Firefox 131 on macOS").And.Contain("1440 × 900").And.Contain("de");
    }

    [Fact]
    public async Task AnIdea_IsFiledAsTheIdeaTypeWithItsTextInTheDescription()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report["kind"] = "idea"), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.WorkItemCreation.Path.Should().EndWith("/_apis/wit/workitems/$User%20Story");
        var patch = Patch(factory);
        Field(patch, "System.Description").Should().Contain("The map never drew.");
        Field(patch, "Microsoft.VSTS.TCM.ReproSteps").Should().BeNull();
    }

    [Fact]
    public async Task AScreenshot_IsUploadedBeforeTheWorkItemAndShownAndAttachedInIt()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report["screenshot"] = DataAddress("image/png", Picture)), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var requests = factory.DevOpsRequests;
        requests.Should().HaveCount(2);
        requests[0].Path.Should().Be("/Example/Clients/_apis/wit/attachments");
        requests[0].Address.Query.Should().Contain("fileName=screenshot.png");
        requests[0].ContentType.Should().Be("application/octet-stream");
        requests[0].Body.Should().Equal(Picture);

        var attachment = $"{FeedbackWebApplicationFactory.Organisation}/_apis/wit/attachments/a1";
        var patch = Patch(factory);
        Field(patch, "Microsoft.VSTS.TCM.ReproSteps").Should().Contain($"<img src=\"{attachment}\"");
        var link = patch.Single(operation => (string?)operation!["path"] == "/relations/-")!["value"]!;
        ((string?)link["rel"]).Should().Be("AttachedFile");
        ((string?)link["url"]).Should().Be(attachment);
    }

    [Fact]
    public async Task AReportWithoutAScreenshot_UploadsNothing()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        await Post(factory, Report(), token);

        factory.DevOpsRequests.Should().ContainSingle().Which.Path.Should().Contain("/_apis/wit/workitems/");
        Patch(factory).Should().NotContain(operation => (string?)operation!["path"] == "/relations/-");
    }

    [Fact]
    public async Task AScreenshotThatIsNotAPicture_IsRefused()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(
            factory, Report(report => report["screenshot"] = DataAddress("text/html", "<p>hi</p>"u8.ToArray())), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be("screenshotUnreadable");
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AServiceToken_FilesUnderTheWorkerItNamesAndSaysThroughWhichService()
    {
        var token = ServiceToken("kiosk-gateway");
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report["reporter"] = "W-12"), token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = Field(Patch(factory), "Microsoft.VSTS.TCM.ReproSteps")!;
        body.Should().Contain("W-12").And.Contain("kiosk-gateway");
    }

    [Fact]
    public async Task AServiceTokenNamingNoReporter_IsRefused()
    {
        var token = ServiceToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be("reporterMissing");
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task APersonCannotFileUnderSomebodyElsesName()
    {
        var token = PersonToken("ada");
        await using var factory = RelayAccepting(token);

        await Post(factory, Report(report => report["reporter"] = "mallory"), token);

        var body = Field(Patch(factory), "Microsoft.VSTS.TCM.ReproSteps")!;
        body.Should().Contain("ada").And.NotContain("mallory");
    }

    [Fact]
    public async Task AnApplicationTheSettingsDoNotName_IsRefusedAndNothingReachesDevOps()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report["application"] = "Elsewhere"), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be("applicationUnknown");
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("title", "", "fieldMissing")]
    [InlineData("title", "   ", "fieldMissing")]
    [InlineData("kind", "question", "kindUnknown")]
    public async Task AReportMissingWhatItNeeds_IsRefusedWithACode(string field, string value, string code)
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report[field] = value), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be(code);
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("description", 20_001)]
    [InlineData("browser", 2_001)]
    public async Task TextLongerThanTheRelayTakes_IsRefused(string field, int characters)
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report =>
        {
            if (field == "description") report["description"] = new string('a', characters);
            else report["context"]![field] = new string('a', characters);
        }), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be("fieldTooLong");
    }

    [Fact]
    public async Task AReportNamingNoApplication_IsRefused()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report.Remove("application")), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Body(response);
        body.GetProperty("code").GetString().Should().Be("fieldMissing");
        body.GetProperty("values").GetProperty("field").GetString().Should().Be("application");
    }

    [Fact]
    public async Task ABodyThatIsNotAReport_IsRefusedAsUnreadable()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/reports")
        {
            Content = new StringContent("{ not json", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be("reportUnreadable");
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AJpegScreenshot_IsStoredUnderAJpegName()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        await Post(factory, Report(report => report["screenshot"] = DataAddress("image/jpeg", Picture)), token);

        factory.DevOpsRequests[0].Address.Query.Should().Contain("fileName=screenshot.jpg");
    }

    [Fact]
    public async Task AScreenshotWhosePictureDataIsBroken_IsRefused()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report["screenshot"] = "data:image/png;base64,abc"), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Body(response)).GetProperty("code").GetString().Should().Be("screenshotUnreadable");
    }

    [Fact]
    public async Task DevOpsAnsweringWithAPageInsteadOfJson_AnswersBadGateway()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);
        factory.DevOpsAnswers = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Sign in to Azure DevOps</html>", Encoding.UTF8, "text/html"),
        };

        var response = await Post(factory, Report(), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        factory.Log.Lines.Should().Contain(line => line.Contains("not JSON"));
    }

    [Fact]
    public async Task ATitleLongerThanDevOpsTakes_IsRefused()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(report => report["title"] = new string('a', 256)), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Body(response);
        body.GetProperty("code").GetString().Should().Be("fieldTooLong");
        body.GetProperty("values").GetProperty("characters").GetInt32().Should().Be(255);
    }

    [Fact]
    public async Task MarkupInWhatWasWritten_ReachesTheWorkItemAsTextAndNeverAsMarkup()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        await Post(factory, Report(report =>
        {
            report["description"] = "<img src=x onerror=alert(1)>";
            report["context"]!["pageAddress"] = "javascript:alert(1)";
        }), token);

        var body = Field(Patch(factory), "Microsoft.VSTS.TCM.ReproSteps")!;
        body.Should().Contain("&lt;img src=x onerror=alert(1)&gt;");
        body.Should().NotContain("<img src=x");
        body.Should().NotContain("href=\"javascript:");
    }

    [Fact]
    public async Task DevOpsRefusingTheWorkItem_AnswersBadGatewayAndKeepsItsDetailInTheLog()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);
        factory.DevOpsAnswers = request => request.Path.Contains("/_apis/wit/workitems/")
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("TF401320: Rule Error for field Area Path", Encoding.UTF8, "text/plain"),
            }
            : null;

        var response = await Post(factory, Report(), token);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("filingFailed").And.NotContain("TF401320");
        factory.Log.Lines.Should().Contain(line => line.Contains("TF401320"));
    }

    [Fact]
    public async Task ThePlatformUnreachable_AnswersServiceUnavailableAndNothingReachesDevOps()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);
        factory.PlatformUnreachable = true;

        var response = await Post(factory, Report(), token);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Body(response)).GetProperty("code").GetString().Should().Be("serviceUnavailable");
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ABodyOverTheLimit_IsRefusedBeforeAnyoneIsAsked()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var tooLarge = new byte[ReportLimits.MaximumBodyBytes];
        var response = await Post(factory, Report(report => report["screenshot"] = DataAddress("image/png", tooLarge)), token);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        factory.PlatformRequests.Should().BeEmpty();
        factory.DevOpsRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task MoreReportsFromOneAddressThanTheRateAllows_AreToldWhenToComeBack()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        for (var sent = 0; sent < ReportRate.RequestsAllowed; sent++)
            (await Post(factory, Report(), token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var refused = await Post(factory, Report(), token);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.Should().NotBeNull();
        (await Body(refused)).GetProperty("code").GetString().Should().Be("tooManyRequests");
    }

    [Fact]
    public async Task APreflightFromAnAllowedOrigin_MayCarryTheSignInToken()
    {
        await using var factory = new FeedbackWebApplicationFactory { AllowedOrigin = "https://console.example.org" };
        using var client = factory.CreateClient();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/reports");
        preflight.Headers.Add("Origin", "https://console.example.org");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(preflight);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://console.example.org");
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().ToLowerInvariant().Should().Contain("authorization");
    }

    [Fact]
    public async Task WithNoOriginAllowed_ACrossOriginCallerIsNotLetIn()
    {
        var token = PersonToken();
        await using var factory = RelayAccepting(token);

        var response = await Post(factory, Report(), token, origin: "https://elsewhere.example.org");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
