namespace vos.Pages;

/// <summary>
/// What sending a press asks of the model, write by write, in the order the press makes them. A failed
/// write is thrown and stops the press, so a refused press is never stamped.
/// <para>
/// The path of a press comes out of a page description, which is the model's data, and so do the
/// identifiers of a relate and a stamp. Whoever fills this decides for each write whether to send it.
/// </para>
/// </summary>
public interface IPageWrites
{
    Task<string> PressAsync(string path, string bodyJson);

    Task RelateAsync(string subjectId, string predicateId, string targetId);

    /// <summary>Sets a time property on a Thing; <paramref name="at"/> is an ISO 8601 time in UTC.</summary>
    Task StampAsync(string thingId, string property, string at);

    /// <summary>Told of each post a press made, in the console's own words, and whether it was taken.</summary>
    void RecordAction(string description, bool succeeded);
}
