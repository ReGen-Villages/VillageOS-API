using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using vos.Hypha.Registry;
using vos.Taproot;

namespace vos.Hypha.Tools.Generic;

[McpServerToolType]
public sealed class ModelTools(MyceliumClient mycelium)
{
    [McpServerTool(Name = "list_models", Title = "List models", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the models the key this server was given may reach, each with its identifier and name.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    public Task<JsonElement> ListModelsAsync() => mycelium.ListModelsAsync();
}
