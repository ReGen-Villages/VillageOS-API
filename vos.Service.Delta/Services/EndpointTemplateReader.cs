using System.Text.Json;
using vos.Service.Delta.Models;
using vos.Service.Shared.Subscriptions;

namespace vos.Service.Delta.Services;

public sealed record EndpointTemplate(Guid Id, string Name, Guid IsPredicateId, IReadOnlyDictionary<string, JsonElement> Properties);

// Read on every registration and kept for none, so a template added to or changed in a live model is what the next
// registration is checked against. Both reads go out under the caller's bearer, so each registration reads its own model.
public sealed class EndpointTemplateReader(
    ISubscriptionClient subscriptions, MyceliumClient mycelium, ILogger<EndpointTemplateReader> logger)
{
    public async Task<(EndpointTemplate? Template, TemplateRefusal? Refusal)> ReadAsync(string? templateName)
    {
        SubscribeResult subscribed;
        try
        {
            subscribed = await subscriptions.SubscribeAsync(EndpointTemplateTree.Selector(templateName));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to read the endpoint templates from the model");
            return (null, new TemplateRefusal(500, "Could not read the endpoint templates from the model."));
        }

        LocatedTemplate? located;
        TemplateRefusal? refusal;
        try
        {
            (located, refusal) = EndpointTemplateTree.Locate(subscribed.Snapshot, templateName);
        }
        finally
        {
            await ReleaseAsync(subscribed.SubscriptionId);
        }
        if (located is null) return (null, refusal);

        var properties = await mycelium.GetResolvedPropertiesAsync(located.Id);
        if (properties is null)
            return (null, new TemplateRefusal(500, $"Could not read the properties of the endpoint template '{located.Name}'."));

        return (new EndpointTemplate(located.Id, located.Name, located.IsPredicateId, properties), null);
    }

    private async Task ReleaseAsync(Guid subscriptionId)
    {
        try
        {
            await subscriptions.UnsubscribeAsync(subscriptionId);
        }
        catch (Exception exception)
        {
            // The read already succeeded; failing to release the subscription must not refuse the registration.
            logger.LogWarning(exception, "Failed to release the template-read subscription {SubscriptionId}", subscriptionId);
        }
    }
}
