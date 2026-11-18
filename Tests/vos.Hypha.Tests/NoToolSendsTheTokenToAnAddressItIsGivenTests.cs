using FluentAssertions;
using vos.Taproot;
using vos.Tests.Shared;
using Xunit;

namespace vos.Mcp.Tests;

public class NoToolSendsTheTokenToAnAddressItIsGivenTests
{
    private const string Reason =
        "the ingest upload sends the token to an address its caller gives, so the server may not use it until "
        + "a design says where that address comes from: the server's configuration or the model through a "
        + "role, never a tool's arguments";

    [Fact]
    public void No_source_of_the_server_names_the_ingest_upload()
    {
        var sources = ServerSources().ToList();

        sources.Should().Contain(source => Path.GetFileName(source) == "ServerProgram.cs",
            "the scan must reach the server's sources, or it passes over nothing");
        sources.Where(source => NamesTheIngestUpload(File.ReadAllText(source)))
            .Select(Path.GetFileName)
            .Should().BeEmpty(Reason);
    }

    [Theory]
    [InlineData("await mycelium.IngestIfcAsync(address, file, name, mode);", true)]
    [InlineData("var upload = nameof(MyceliumClient.IngestIfcAsync);", true)]
    [InlineData("await mycelium.ListModelsAsync();", false)]
    public void A_source_is_found_to_name_the_ingest_upload_however_it_is_written(string source, bool names) =>
        NamesTheIngestUpload(source).Should().Be(names);

    private static bool NamesTheIngestUpload(string source) =>
        source.Contains(nameof(MyceliumClient.IngestIfcAsync), StringComparison.Ordinal);

    private static IEnumerable<string> ServerSources() =>
        new DirectoryInfo(Path.Combine(RepositoryRoot.Find(), "vos.Mcp"))
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Select(file => file.FullName)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"));
}
