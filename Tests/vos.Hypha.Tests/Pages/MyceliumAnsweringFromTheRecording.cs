using System.Net;
using System.Text.Json;

namespace vos.Hypha.Tests.Pages;

// A request the recording holds no answer for is answered 404.
internal static class MyceliumAnsweringFromTheRecording
{
    private static readonly JsonElement s_recording = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Pages", "catchments.recorded.json"))).RootElement;

    public static JsonElement FirstPage => s_recording.GetProperty("pages")[0];

    public static JsonElement FirstThingWithFigures => s_recording.GetProperty("things")[0];

    public static HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var path = request.RequestUri!.PathAndQuery;
        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

        if (request.Method == HttpMethod.Post && path == "/api/snapshots")
            return s_recording.GetProperty("selections").TryGetProperty(body!, out var selected)
                ? ServerUnderTest.Json($$"""{"watermark":1,"snapshot":{{selected.GetRawText()}}}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound);

        var asked = request.Method == HttpMethod.Get ? $"GET {path}" : $"POST {path} {body}";
        if (s_recording.GetProperty("reads").TryGetProperty(asked, out var answer)) return ServerUnderTest.Json(answer.GetString()!);
        return ServerUnderTest.Json("""{"error":"Nothing answers there"}""", HttpStatusCode.NotFound);
    }
}
