using System.Text.Json.Serialization;

namespace vos.Service.Forage.Models;

public class DiscoveryRequest
{
    // The Site by id rather than by name: every caller — a planner action, a submission arriving, a
    // scheduled refresh — already holds the Thing it selected, and a name lookup would buy a second
    // broker client for nothing.
    [JsonPropertyName("siteId")]
    public Guid SiteId { get; set; }
}
