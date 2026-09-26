using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace vos.Service.Feedback;

// What DevOps answered when it did not do what was asked. Kept for the log, and never shown to a
// reporter: it names projects, fields and rules of an organisation the reporter may not belong to.
public sealed class DevOpsRefusedError(int status, string detail)
    : Exception($"Azure DevOps answered {status}: {detail}");

public sealed class DevOpsWorkItems(IHttpClientFactory clients, Uri organisation, string accessToken)
{
    private const string ApiVersion = "7.1";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private sealed record StoredAttachment(string Url);

    private sealed record CreatedWorkItem(int Id);

    private static readonly JsonSerializerOptions AnswerFormat = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> AttachAsync(string project, Screenshot screenshot, CancellationToken cancellation)
    {
        var content = new ByteArrayContent(screenshot.Bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var stored = await SendAsync<StoredAttachment>(
            $"{ProjectAddress(project)}/_apis/wit/attachments?fileName={screenshot.FileName}&api-version={ApiVersion}",
            content, cancellation);
        return stored.Url;
    }

    public async Task<int> CreateAsync(string project, string workItemType, JsonArray patch, CancellationToken cancellation)
    {
        var content = new StringContent(patch.ToJsonString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");

        var created = await SendAsync<CreatedWorkItem>(
            $"{ProjectAddress(project)}/_apis/wit/workitems/${Uri.EscapeDataString(workItemType)}?api-version={ApiVersion}",
            content, cancellation);
        return created.Id;
    }

    private string ProjectAddress(string project) => $"{organisation.ToString().TrimEnd('/')}/{Uri.EscapeDataString(project)}";

    private async Task<T> SendAsync<T>(string address, HttpContent content, CancellationToken cancellation)
    {
        using var client = clients.CreateClient();
        client.Timeout = Patience;
        using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{accessToken}")));

        using var response = await client.SendAsync(request, cancellation);
        var text = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
            throw new DevOpsRefusedError((int)response.StatusCode, text);

        try
        {
            return JsonSerializer.Deserialize<T>(text, AnswerFormat)
                   ?? throw new DevOpsRefusedError((int)response.StatusCode, "an empty answer");
        }
        catch (JsonException)
        {
            // A sign-in page instead of JSON is what DevOps answers to an expired access token.
            throw new DevOpsRefusedError((int)response.StatusCode, $"an answer that is not JSON: {text[..Math.Min(text.Length, 200)]}");
        }
    }
}
