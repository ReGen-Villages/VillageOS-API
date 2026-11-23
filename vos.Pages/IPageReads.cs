namespace vos.Pages;

// A path arriving here comes out of a page description, which is the model's data, so whoever fills this sends only the routes a page read needs and refuses every other path.
public interface IPageReads
{
    Task<SelectedThings> SelectAsync(string selectorJson, CancellationToken cancellation);

    Task<string> GetAsync(string path, CancellationToken cancellation);

    Task<string> PostAsync(string path, string bodyJson, CancellationToken cancellation);
}

public sealed record SelectedThings(string ThingsJson, string RelationshipsJson);
