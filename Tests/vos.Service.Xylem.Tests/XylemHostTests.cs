using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using vos.Service.Xylem.Services;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Xylem.Tests;

// What the host composes from the handler, the job store and the credential check: the routes a
// caller reaches, the replies each shape of upload gets, and which routes a verification key closes.
public class XylemHostTests
{
    private const string Issuer = "VillageOS";
    private const string Audience = "xylem-handler";

    private static MultipartFormDataContent Upload(string content, string name = "Demo", string? mode = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(content), "file", "building.ifc" },
            { new StringContent(name), "name" },
        };
        if (mode != null) form.Add(new StringContent(mode), "mode");
        return form;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Health_and_stats_answer_without_a_credential()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var health = await BodyOf(await client.GetAsync("/health"));
        var stats = await BodyOf(await client.GetAsync("/stats"));

        health.GetProperty("status").GetString().Should().Be("Healthy");
        health.GetProperty("service").GetString().Should().Be("Xylem");
        stats.GetProperty("myceliumUrl").GetString().Should().Be(XylemWebApplicationFactory.MyceliumUrl);
    }

    [Fact]
    public async Task An_upload_that_is_not_a_form_is_refused()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/ingest", new { file = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).GetProperty("error").GetString().Should().Contain("multipart/form-data");
        factory.Runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_form_without_a_file_is_refused()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();
        var form = new MultipartFormDataContent { { new StringContent("Demo"), "name" } };

        var response = await client.PostAsync("/ingest", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).GetProperty("error").GetString().Should().Contain("No IFC file");
        factory.Runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_merge_upload_runs_the_tool_and_answers_its_counts()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/ingest", Upload("ISO-10303-21;", name: "Willow Bend"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(response);
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("thingsCreated").GetInt32().Should().Be(3);
        body.GetProperty("relationshipsCreated").GetInt32().Should().Be(2);
        factory.Runner.SeenName.Should().Be("Willow Bend");
        factory.Runner.SeenBytes.Should().Be("ISO-10303-21;".Length);
        factory.Preparer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_run_whose_counts_were_not_read_is_answered_with_null_for_each_count()
    {
        await using var factory = new XylemWebApplicationFactory();
        factory.Runner.Result = new IngestRunResult(true, null, null, null, null);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/ingest", Upload("ISO-10303-21;"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyOf(response);
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("thingsCreated").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("thingsUpdated").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("relationshipsCreated").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_new_model_upload_clears_the_model_first()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/ingest", Upload("ISO-10303-21;", mode: "new-model"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Preparer.Calls.Should().Be(1);
        factory.Runner.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_run_is_answered_as_a_refusal_carrying_its_reason()
    {
        await using var factory = new XylemWebApplicationFactory();
        factory.Runner.Result = new IngestRunResult(false, 0, 0, 0, "IFC schema not recognised");
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/ingest", Upload("ISO-10303-21;"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).GetProperty("error").GetString().Should().Be("IFC schema not recognised");
    }

    [Fact]
    public async Task An_asynchronous_upload_answers_a_job_that_completes_with_the_run()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var accepted = await client.PostAsync("/ingest?async=true", Upload("ISO-10303-21;"));

        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var jobId = (await BodyOf(accepted)).GetProperty("jobId").GetString()!;
        accepted.Headers.Location!.ToString().Should().EndWith($"/ingest/jobs/{jobId}");

        IngestJob? job = null;
        await Settle.UntilAsync(
            () =>
            {
                job = client.GetFromJsonAsync<IngestJob>($"/ingest/jobs/{jobId}").GetAwaiter().GetResult();
                return job?.Status == IngestJobStatus.Succeeded;
            },
            "the background ingest completes");
        job!.Result!.ThingsCreated.Should().Be(3);
        factory.Runner.SeenName.Should().Be("Demo");
    }

    [Fact]
    public async Task An_asynchronous_upload_of_nothing_is_refused_and_starts_no_job()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/ingest?async=true", Upload(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyOf(response)).GetProperty("error").GetString().Should().Contain("No IFC content");
        factory.Runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_job_is_not_found()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/ingest/jobs/no-such-job");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Shutdown_answers_before_stopping()
    {
        await using var factory = new XylemWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/shutdown", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOf(response)).GetProperty("message").GetString().Should().Contain("Shutting down");
    }

    [Fact]
    public async Task A_verification_key_closes_every_route_but_health_and_stats()
    {
        var signer = new MyceliumSigner();
        await using var factory = new XylemWebApplicationFactory
        {
            VerificationKey = signer.VerificationKey, Issuer = Issuer, Audience = Audience,
        };
        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/stats")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/ingest", Upload("ISO-10303-21;"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/ingest/jobs/any")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsync("/shutdown", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Runner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_token_the_platform_signed_opens_the_ingest_route()
    {
        var signer = new MyceliumSigner();
        await using var factory = new XylemWebApplicationFactory
        {
            VerificationKey = signer.VerificationKey, Issuer = Issuer, Audience = Audience,
        };
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signer.Token(Issuer, Audience));

        var response = await client.PostAsync("/ingest", Upload("ISO-10303-21;"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Runner.Calls.Should().Be(1);
    }
}
