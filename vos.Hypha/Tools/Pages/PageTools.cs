using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using vos.Hypha.Registry;
using vos.Pages;
using vos.PlatformClient;

namespace vos.Hypha.Tools.Pages;

[McpServerToolType]
public sealed class PageTools(ModelClients clients, PageReader reader)
{
    private const string DefaultLanguage = "en";
    private const string WhichModel = "The model to read, by its identifier or its name, as list_models gives them. The key's own model where none is given.";

    [McpServerTool(Name = "list_pages", Title = "List pages", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the pages the model publishes: each page's identifier and name, whether its description could be read, "
        + "and the Things it compares, any of which read_page can be asked to read it for.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    public Task<JsonElement> ListPagesAsync(
        [Description("The language names are ordered in, as a language tag such as en or de. English where none is given.")]
        string? language = null,
        [Description(WhichModel)]
        string? model = null,
        CancellationToken cancellation = default) =>
        InWordsAsync(async () =>
        {
            var reads = new MyceliumPageReads(await clients.ForAsync(model));
            var asked = language ?? DefaultLanguage;
            var pages = new JsonArray();
            foreach (var page in await reader.ListPagesAsync(asked, reads, cancellation))
            {
                var scopes = page.Readable ? await reader.ScopesOfAsync(page.Id, asked, reads, cancellation) : [];
                pages.Add(new JsonObject
                {
                    ["id"] = page.Id,
                    ["name"] = page.Name,
                    ["readable"] = page.Readable,
                    ["compares"] = new JsonArray(scopes.Select(scope => (JsonNode)new JsonObject { ["id"] = scope.Id, ["name"] = scope.Name }).ToArray()),
                });
            }
            return Answered(new JsonObject { ["pages"] = pages }, reads);
        });

    [McpServerTool(Name = "read_page", Title = "Read a page", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Reads one page the model publishes, in words. Each widget comes back in order: a figure with its value, its value as "
        + "the page writes it, its unit and where it came from; a verdict as the model's own sentences and the inputs it offers "
        + "to change; a worked figure with its formula and inputs. A chart or a table comes back as its rows, with no sentence. "
        + "The sentences are the model's own wording, which only a page holds.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    public Task<JsonElement> ReadPageAsync(
        [Description("The page, by its identifier or its name, as list_pages gives them.")]
        string page,
        [Description(
            "The Things to read the page for, each by its identifier or its name, from those list_pages says the page compares. "
            + "Leave out to read the page across everything it compares.")]
        string[]? scopes = null,
        [Description("The language the page is worded in, as a language tag such as en or de. English where none is given.")]
        string? language = null,
        [Description(WhichModel)]
        string? model = null,
        CancellationToken cancellation = default) =>
        InWordsAsync(async () =>
        {
            var reads = new MyceliumPageReads(await clients.ForAsync(model));
            var asked = language ?? DefaultLanguage;
            var (pageId, scopeIds) = await PageAndScopesAsync(reader, page, scopes ?? [], asked, reads, cancellation);
            var readings = await reader.ReadPageAsync(pageId, scopeIds, asked, reads, cancellation);
            return Answered(new JsonObject { ["readings"] = new JsonArray(readings.Select(reading => JsonNode.Parse(reading)).ToArray()) }, reads);
        });

    [McpServerTool(Name = "explain", Title = "Explain a figure", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Explains one figure of one Thing from the model alone, as data: its value, each range that judges it with the state "
        + "it names and whether the Thing holds that state, what it was worked out from, and its inputs with where each value "
        + "is held and which way the figure moves with it. It gives no sentence: the model's own wording for a figure is "
        + "written in a page, which read_page reads.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    public Task<JsonElement> ExplainAsync(
        [Description("The Thing, by its identifier or its name.")]
        string thing,
        [Description("The name of the property to explain, as the model names it.")]
        string figure,
        [Description("The language numbers and input names are written in, as a language tag such as en or de. English where none is given.")]
        string? language = null,
        [Description(WhichModel)]
        string? model = null,
        CancellationToken cancellation = default) =>
        InWordsAsync(async () =>
        {
            var mycelium = await clients.ForAsync(model);
            var resolved = await new NameResolver(mycelium).FindThingAsync(thing);
            if (!resolved.IsSuccess) throw new McpException(resolved.ErrorMessage ?? $"No Thing is named \"{thing}\".");

            var reads = new MyceliumPageReads(mycelium);
            var explained = await reader.ExplainAsync(resolved.Id.ToString(), figure, language ?? DefaultLanguage, reads, cancellation);
            return Answered(JsonNode.Parse(explained)!.AsObject(), reads);
        });

    /// <summary>A refusal by the reader reaches the chat application in the reader's words.</summary>
    internal static async Task<JsonElement> InWordsAsync(Func<Task<JsonElement>> read)
    {
        try
        {
            return await read();
        }
        catch (PageNotReadException refused)
        {
            throw new McpException(refused.Message);
        }
    }

    /// <summary>With no Thing named, the page is read across everything it compares.</summary>
    internal static async Task<(string PageId, IReadOnlyList<string?> ScopeIds)> PageAndScopesAsync(
        PageReader reader, string page, IReadOnlyList<string> scopes, string language, MyceliumPageReads reads, CancellationToken cancellation)
    {
        var found = OneNamed(page, await reader.ListPagesAsync(language, reads, cancellation), one => one.Id, one => one.Name, "page");
        if (!found.Readable) throw new McpException($"The description of the page \"{found.Name}\" could not be read, so the page cannot be.");
        if (scopes.Count == 0) return (found.Id, [null]);

        var compared = await reader.ScopesOfAsync(found.Id, language, reads, cancellation);
        return (found.Id, scopes.Select(scope => (string?)OneNamed(scope, compared, one => one.Id, one => one.Name, "Thing the page compares").Id).ToList());
    }

    internal static T OneNamed<T>(string asked, IReadOnlyList<T> among, Func<T, string> idOf, Func<T, string> nameOf, string what)
    {
        var byId = among.Where(one => string.Equals(idOf(one), asked, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count == 1) return byId[0];

        var byName = among.Where(one => nameOf(one) == asked).ToList();
        return byName.Count switch
        {
            1 => byName[0],
            0 => throw new McpException($"No {what} has the identifier or the name \"{asked}\"."),
            _ => throw new McpException(
                $"More than one {what} is named \"{asked}\". Ask by identifier: {string.Join(", ", byName.Select(idOf))}."),
        };
    }

    internal static JsonElement Answered(JsonObject answer, MyceliumPageReads reads)
    {
        if (!reads.Refused.IsEmpty) answer["notAsked"] = new JsonArray(reads.Refused.Distinct().Select(refusal => (JsonNode)refusal).ToArray());
        return JsonSerializer.SerializeToElement(answer);
    }
}
