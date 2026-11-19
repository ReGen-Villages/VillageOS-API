namespace vos.Pages;

/// <summary>The object the bundle sends a press's writes through.</summary>
public sealed class WritesForTheEngine(IPageWrites writes)
{
    // Lower case because JavaScript calls them by these names.
    public Task<string> press(string path, string bodyJson) => writes.PressAsync(path, bodyJson);

    public Task relate(string subjectId, string predicateId, string targetId) => writes.RelateAsync(subjectId, predicateId, targetId);

    public Task stamp(string thingId, string property, string at) => writes.StampAsync(thingId, property, at);

    public void recordAction(string description, bool succeeded) => writes.RecordAction(description, succeeded);
}
