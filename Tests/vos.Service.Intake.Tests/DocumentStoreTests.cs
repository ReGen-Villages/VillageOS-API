using FluentAssertions;
using vos.Service.Intake.Services;
using Xunit;

namespace vos.Service.Intake.Tests;

public sealed class DocumentStoreTests : IDisposable
{
    private const string SubmissionId = "4d1c2b3a-5e6f-4a7b-8c9d-0e1f2a3b4c5d";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "intake-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private async Task<string> SaveAsync(DocumentStore store, string fileName) =>
        (await store.SaveAsync(SubmissionId, fileName, new MemoryStream([1, 2, 3]), CancellationToken.None)).StoredAs;

    [Fact]
    public async Task Removing_one_file_leaves_the_submissions_other_files()
    {
        var store = new DocumentStore(_folder);
        var removed = await SaveAsync(store, "layers.kml");
        var kept = await SaveAsync(store, "survey.pdf");

        store.Remove(SubmissionId, removed);

        Directory.GetFiles(Path.Combine(_folder, SubmissionId)).Select(Path.GetFileName).Should().Equal(kept);
    }

    [Fact]
    public void Removing_a_file_that_is_already_gone_leaves_nothing_to_do()
    {
        new DocumentStore(_folder).Invoking(store => store.Remove(SubmissionId, "0f9e8d7c.kml")).Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../other/layers.kml")]
    public async Task A_key_that_is_not_a_plain_file_name_is_refused_and_nothing_is_removed(string storedAs)
    {
        var store = new DocumentStore(_folder);
        var kept = await SaveAsync(store, "layers.kml");

        store.Invoking(it => it.Remove(SubmissionId, storedAs)).Should().Throw<ArgumentException>();
        File.Exists(Path.Combine(_folder, SubmissionId, kept)).Should().BeTrue();
    }
}
