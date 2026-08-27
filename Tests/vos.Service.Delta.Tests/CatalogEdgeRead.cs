using System.Net;
using System.Text;

namespace vos.Service.Delta.Tests;

/// <summary>The read every provisioning run makes before it writes anything: which edges the endpoint
/// template catalog already carries. Answering no edges describes a model where nothing is wired yet,
/// which is where these tests start — a fake that does not answer at all makes provisioning refuse,
/// which is a different subject entirely.</summary>
internal static class CatalogEdgeRead
{
    internal static HttpResponseMessage? Answer(HttpRequestMessage request) => Answer(request, []);

    internal static HttpResponseMessage? Answer(
        HttpRequestMessage request, IEnumerable<(Guid Subject, Guid Predicate, Guid Target)> edges)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Delete && path.StartsWith("/api/subscriptions/", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        if (request.Method != HttpMethod.Post || path != "/api/subscriptions")
            return null;

        var relationships = edges.Select(edge =>
            "{\"id\":\"" + Guid.NewGuid() + "\",\"name\":null,\"subjectId\":\"" + edge.Subject
            + "\",\"predicateId\":\"" + edge.Predicate + "\",\"targetId\":\"" + edge.Target
            + "\",\"properties\":{},\"inheritedProperties\":{},\"states\":[]}");

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"subscriptionId\":\"" + Guid.NewGuid() + "\",\"watermark\":0,\"snapshot\":{"
                + "\"watermark\":0,\"things\":[],\"relationships\":[" + string.Join(",", relationships) + "]}}",
                Encoding.UTF8, "application/json"),
        };
    }
}
