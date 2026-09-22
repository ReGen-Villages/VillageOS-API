namespace vos.Service.Intake.Services;

public sealed record StoredDocument(string StoredAs, long SizeBytes);

// The bytes of the files submitters share, kept under one folder keyed by submission — the model holds
// what can be listed and judged and never reads inside a file, so the file itself lives beside the
// service that took it. A submission's folder goes when the model no longer holds the submission.
public sealed class DocumentStore(string directory)
{
    public string Directory { get; } = directory;

    // Keeps the bytes under a key of the store's own: the name a person gave the file is a
    // value on the Thing, never a path, so two files of one name and a name with a slash in it are
    // both kept.
    public async Task<StoredDocument> SaveAsync(string submissionId, string fileName, Stream bytes, CancellationToken cancellation)
    {
        var folder = FolderOf(submissionId);
        System.IO.Directory.CreateDirectory(folder);
        var storedAs = Guid.NewGuid().ToString("N") + Path.GetExtension(fileName);
        await using var file = File.Create(Path.Combine(folder, storedAs));
        await bytes.CopyToAsync(file, cancellation);
        return new StoredDocument(storedAs, file.Length);
    }

    public IReadOnlyList<string> SubmissionsHeld() =>
        System.IO.Directory.Exists(Directory)
            ? [.. System.IO.Directory.EnumerateDirectories(Directory).Select(Path.GetFileName).OfType<string>()]
            : [];

    // Takes a submission's files out, whole. Nothing is kept of a submission the model has let go.
    public void Forget(string submissionId)
    {
        var folder = FolderOf(submissionId);
        if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, recursive: true);
    }

    private string FolderOf(string submissionId)
    {
        // A submission's reference is the service's own, minted as a name-safe token; anything else
        // reaching here would be a path, and a path is refused rather than resolved.
        if (submissionId.Length == 0 || submissionId.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("A submission reference is letters, digits and hyphens.", nameof(submissionId));
        return Path.Combine(Directory, submissionId);
    }
}
