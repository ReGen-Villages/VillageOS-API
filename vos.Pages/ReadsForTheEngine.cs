namespace vos.Pages;

public sealed class ReadsForTheEngine(IPageReads reads, CancellationToken cancellation)
{
    // Lower case because JavaScript calls them by these names.
    public Task<string> get(string path) => reads.GetAsync(path, cancellation);

    public Task<string> post(string path, string bodyJson) => reads.PostAsync(path, bodyJson, cancellation);
}
