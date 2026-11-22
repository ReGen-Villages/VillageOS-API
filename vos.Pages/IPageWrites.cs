namespace vos.Pages;

// A failed write is thrown and stops the press, so a refused press is never stamped.
public interface IPageWrites
{
    Task<string> PressAsync(string path, string bodyJson);

    Task RelateAsync(string subjectId, string predicateId, string targetId);

    // at is an ISO 8601 time in UTC.
    Task StampAsync(string thingId, string property, string at);

    void RecordAction(string description, bool succeeded);
}
