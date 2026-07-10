namespace vos.ManagedMicroservice.Xylem.Services;

/// <summary>Streams an upload to a temp file, enforcing the size cap as it copies (#5845) so a very large
/// IFC is never held whole in memory and an over-cap upload is stopped early rather than fully buffered.</summary>
public static class UploadSpooler
{
    /// <summary>Copy <paramref name="src"/> to <paramref name="path"/>; returns bytes written, or -1 once
    /// the running total exceeds <paramref name="maxBytes"/> (the copy stops at that point).</summary>
    public static async Task<long> SpoolAsync(Stream src, string path, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        await using var fs = File.Create(path);
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > maxBytes) return -1;
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return total;
    }
}
