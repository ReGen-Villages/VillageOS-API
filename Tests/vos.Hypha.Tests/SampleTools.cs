using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using vos.Hypha.Registry;
using vos.Taproot;

namespace vos.Hypha.Tests;

// The server's first tool needs no editor's policy and no bound role, so these exercise those rules.
[McpServerToolType]
public sealed class SampleTools(MyceliumClient mycelium)
{
    public const string BoundRole = "5f0c4a56-3f6c-5d0b-9a55-0d2f1b6a7c11";

    [McpServerTool(Name = "sample_rename", Idempotent = true)]
    [Description("Renames a Thing.")]
    [RequiresPolicy(MyceliumPolicy.EditorOrAdmin)]
    public async Task<string> RenameAsync(Guid thing, string name) =>
        await mycelium.RenameThingAsync(thing, name) ? "renamed" : "not found";

    [McpServerTool(Name = "sample_delete", Destructive = true)]
    [Description("Deletes a Thing after the person has seen what will go.")]
    [RequiresPolicy(MyceliumPolicy.EditorOrAdmin)]
    public async Task<string> DeleteAsync(
        RequestContext<CallToolRequestParams> call, Guid thing, CancellationToken cancellationToken,
        [Description("The digest a first call answered with.")] string? confirm_digest = null)
    {
        var named = (await mycelium.GetThingAsync(thing))?.GetProperty("Name").GetString();
        var confirmed = await Confirmation.AskAsync(
            call, $"Delete the Thing named {named}.", destroys: true, confirm_digest, cancellationToken);
        if (!confirmed.MayWrite) return confirmed.Refusal;

        await mycelium.DeleteThingAsync(thing);
        return "deleted";
    }

    [McpServerTool(Name = "sample_set", Idempotent = true)]
    [Description("Sets a property after asking the person where the chat application can ask.")]
    [RequiresPolicy(MyceliumPolicy.ModifyData)]
    public async Task<string> SetAsync(
        RequestContext<CallToolRequestParams> call, Guid thing, string value, CancellationToken cancellationToken)
    {
        var confirmed = await Confirmation.AskAsync(
            call, $"Set level to {value}.", destroys: false, digestGiven: null, cancellationToken);
        if (!confirmed.MayWrite) return confirmed.Refusal;

        await mycelium.SetPropertyAsync(thing, "level", "string", value);
        return "set";
    }

    [McpServerTool(Name = "sample_refuses", ReadOnly = true)]
    [Description("Refuses in its own words.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    public static string Refuse() =>
        throw new ModelContextProtocol.McpException("That page holds no widget of that name.");

    [McpServerTool(Name = "sample_read_where_bound", ReadOnly = true)]
    [Description("Reads the Thing a role binds.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    [RequiresBoundRole(BoundRole)]
    public async Task<JsonElement?> ReadWhereBoundAsync() => await mycelium.GetThingBoundByAsync(Guid.Parse(BoundRole));
}
