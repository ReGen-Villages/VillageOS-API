namespace vos.Pages.Tests;

/// <summary>Takes the writes a press sends and keeps them, refusing the one it is told to.</summary>
internal sealed class WritesKeptInMemory : IPageWrites
{
    public List<string> Made { get; } = [];
    public List<(string Description, bool Succeeded)> Reported { get; } = [];
    public List<string> StampedAt { get; } = [];
    public string PressAnswer { get; init; } = "{}";
    public string? RelateRefusal { get; init; }

    public Task<string> PressAsync(string path, string bodyJson, CancellationToken cancellation)
    {
        Made.Add($"press {path}");
        return Task.FromResult(PressAnswer);
    }

    public Task RelateAsync(string subjectId, string predicateId, string targetId, CancellationToken cancellation)
    {
        Made.Add($"relate {subjectId} {predicateId} {targetId}");
        return RelateRefusal is null ? Task.CompletedTask : Task.FromException(new HttpRequestException(RelateRefusal));
    }

    public Task StampAsync(string thingId, string property, string at, CancellationToken cancellation)
    {
        Made.Add($"stamp {thingId} {property}");
        StampedAt.Add(at);
        return Task.CompletedTask;
    }

    public void RecordAction(string description, bool succeeded) => Reported.Add((description, succeeded));
}
