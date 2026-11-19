using ModelContextProtocol.Server;

namespace vos.Hypha.Tests;

// The names to hand this are the whole names of the kinds, predicates, properties and states a model declares
// for itself. It does not catch a part of such a name, a name the platform binds through a role, or the name
// of a member of a kind, which is data.
public static class ToolWording
{
    // A name is looked for anywhere in the text, inside a longer identifier too, because a parameter such as
    // catchmentId names a kind as surely as the word alone does.
    public static IEnumerable<string> NamesOfOneModelIn(IEnumerable<McpServerTool> tools, IEnumerable<string> names)
    {
        var looked = names.ToList();
        foreach (var tool in tools)
        {
            var declared = tool.ProtocolTool;
            var wording = string.Join('\n', declared.Name, declared.Title, declared.Description, declared.InputSchema.GetRawText());
            foreach (var name in looked.Where(name => wording.Contains(name, StringComparison.OrdinalIgnoreCase)))
                yield return $"{declared.Name} holds '{name}', a name that belongs to one model.";
        }
    }
}
