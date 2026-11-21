using ModelContextProtocol.Server;
using vos.Hypha.Registry;

namespace vos.Hypha.Tests;

public static class ToolRules
{
    private static readonly string[] WordsThatDestroy = ["delete", "prune", "promote", "decide"];

    public static IEnumerable<string> Broken(IEnumerable<McpServerTool> tools)
    {
        foreach (var tool in tools)
        {
            var name = tool.ProtocolTool.Name;
            var marks = tool.ProtocolTool.Annotations;
            var policies = tool.Metadata.OfType<RequiresPolicyAttribute>().Select(required => required.Policy).ToList();

            if (policies.Count == 0)
                yield return $"{name} declares no policy, so it would be listed for every key.";

            if (marks?.ReadOnlyHint == true)
                foreach (var writing in policies.Where(policy => policy is not MyceliumPolicy.ReadModel))
                    yield return $"{name} is marked as a tool that only reads and needs {writing}, a policy for writing.";

            if (name.Split('_').Intersect(WordsThatDestroy).Any() && marks?.DestructiveHint != true)
                yield return $"{name} is not marked as destroying, so a chat application would not ask the person first.";
        }
    }
}
