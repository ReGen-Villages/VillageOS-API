using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using vos.Service.Intake.Models;
using vos.Tests.Shared;
using Xunit;

namespace vos.Service.Intake.Tests;

// The pages say a refusal in the reader's language by its code, and fall back to the service's English
// for a code they do not know. A code the service sends and the pages do not list is English on every
// page; a code the pages list and the service never sends is wording nobody reads.
public class RefusalCodeTests
{
    private static readonly string PagesList =
        Path.Combine(RepositoryRoot.Find(), "vos.Trellis", "src", "api", "refusals.ts");

    [Fact]
    public void The_service_and_the_pages_list_the_same_codes()
    {
        var source = File.ReadAllText(PagesList);
        var listed = Regex.Match(source, @"REFUSAL_CODES\s*=\s*\[(?<codes>[^\]]*)\]").Groups["codes"].Value;
        var pageCodes = Regex.Matches(listed, "'([^']+)'").Select(match => match.Groups[1].Value);

        pageCodes.Should().BeEquivalentTo(RefusalCode.All);
    }

    [Fact]
    public void Every_code_is_its_own()
    {
        RefusalCode.All.Should().OnlyHaveUniqueItems();
    }
}

// What a refusal answered: its code and the values its words are filled in with.
internal static class RefusalReading
{
    public static async Task<(string? Code, JsonElement Values)> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.TryGetProperty("code", out var code) ? code.GetString() : null,
            body.TryGetProperty("values", out var values) ? values : default);
    }
}
