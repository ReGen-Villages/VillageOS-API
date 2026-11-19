# Hypha

A server that lets an assistant in a chat application read and change a model. It speaks the Model
Context Protocol (MCP), the protocol a chat application uses to list and call tools on a server. Its
project is `vos.Hypha`.

The server holds no rights of its own. It is given an API key, and the Mycelium decides what that key may
read and write.

## Running it

A chat application starts the server on a person's own machine and talks to it over standard input and
output.

| Setting | Where it comes from |
| --- | --- |
| The Mycelium's address | `--mycelium-url=<address>`, or the `VOS_MYCELIUM_URL` environment variable |
| The API key | The `VOS_API_KEY` environment variable only. An argument list can be read by other programs on the machine, so the key is never an argument |

An entry in a chat application's server list looks like this:

```json
{
  "command": "dotnet",
  "args": ["run", "--project", "vos.Hypha", "--", "--mycelium-url=https://localhost:7243"],
  "env": { "VOS_API_KEY": "<the key issued for this assistant>" }
}
```

**Issue a key for each assistant, and never an administrator's key.** The Mycelium records every change under
the key's account, so a key of its own is what says an assistant made a change. A viewer's key makes the
server read-only: no tool that writes is listed.

Log lines go to standard error, because standard output carries the protocol.

## Tools

| Tool | What it answers | Policy it needs | Listed only where the model binds | Changes anything |
| --- | --- | --- | --- | --- |
| `list_models` | The models the key may reach, each with its identifier and name | ReadModel | — | No |

Whoever adds a tool adds its row here.

## Which tools a key is shown

One key sees one list, decided by its role and its own model's bindings; a tool called with another model
checks that model when it is called.

- The server reads the account's role from the token the Mycelium issues and lists a tool only when that
  role satisfies the Mycelium policy the tool declares.
- A tool that depends on a role is listed only where the model binds that role.
- The list is worked out again each time the Mycelium issues a new token, so a changed role or a newly
  bound role shows within one token's life.
- A call to a tool that is not listed is refused in the same words for every such tool.
- A tool that declares no condition, or that reaches the server by a route the registry cannot read a
  declaration from, is listed for nobody and a call to it is refused.

The Mycelium's own policy check stays the guard. The marks on a tool (only reads, destroys, repeating
changes nothing more) are hints to the chat application, which uses them to decide when to ask the person.

## Adding a tool

A tool is a method on a class marked `[McpServerToolType]`. The server finds every such class in the
program, so adding a tool touches no shared file.

```csharp
[McpServerToolType]
public sealed class ModelTools(MyceliumClient mycelium)
{
    [McpServerTool(Name = "list_models", Title = "List models", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the models the key this server was given may reach, each with its identifier and name.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    public Task<JsonElement> ListModelsAsync() => mycelium.ListModelsAsync();
}
```

| A tool states | With |
| --- | --- |
| Its name, title and description, and what each parameter means | `[McpServerTool]` and `[Description]` |
| Whether it only reads, whether it destroys, whether repeating it changes nothing more | `ReadOnly`, `Destructive`, `Idempotent` on `[McpServerTool]` |
| The Mycelium policy it needs | `[RequiresPolicy]`, required on every tool |
| A role the model must bind for it to be listed | `[RequiresBoundRole]` |
| Any other condition for listing it | A class of your own deriving from `ListingConditionAttribute` |

Tools sit in a folder for each part of the server: `Tools/Generic` for the general tools, `Tools/Pages` for
reading a model's pages, `Tools/Process` for working a Thing through a process.

Rules the tests hold every tool to:

- It declares a policy.
- A tool marked as only reading does not declare a policy for writing.
- A tool that deletes, prunes, promotes or records a decision is marked as destroying.
- No name, parameter or description holds a name that belongs to one model. Anything a tool needs from a
  model is found through a role.

**A tool keeps nothing in memory between two calls that its correctness depends on.** The protocol's newest
revision has no sessions over HTTP, so the next call may reach another process. A cache is allowed.

### A refusal

When the Mycelium refuses a call, let the `HttpRequestException` leave the tool. The server answers the
chat application with a failed tool call carrying the Mycelium's own words, which the assistant can read to
the person.

To refuse for a reason of the tool's own, throw the SDK's `McpException` with the words to answer. The chat
application receives a failed tool call carrying those words after the SDK's own opening,
`An error occurred invoking '<tool>':`. Any other exception reaches it as a bare failure with no reason, so
use this one whenever the person can act on the reason.

### A role in a selector rule

Never pass a role into a rule of a selection (`POST api/snapshots`) without first checking that the model
binds it. The Mycelium does not refuse a rule that names a role the model does not bind, so the tool could
not tell "nothing is related" from "this model has no such role". A tool that depends on a role declares it
with `[RequiresBoundRole]`, which makes that check before the tool runs.

### Asking the person before a change

`Confirmation.AskAsync` takes the text describing what will change and answers whether to write.

| The chat application | What happens |
| --- | --- |
| Speaks the protocol's newest revision | The call is answered with the text, and the chat application calls again with the person's answer |
| Speaks an older revision and can ask the person | The server sends the text as a request of its own and waits for the answer |
| Cannot ask the person, and the change destroys nothing | The change is written at once |
| Cannot ask the person, and the change destroys | The call is answered with the text and a digest of it. A second call carrying that digest in `confirm_digest` writes the change if the text would still be the same; otherwise it is answered with the new text and the new digest |

A tool that destroys takes an optional `confirm_digest` parameter and passes it to `AskAsync`. When the
answer's `MayWrite` is false, return its `Refusal`.

### Reporting a change

After a tool has written, `ActivityReport.ReportAsync` tells the Mycelium what was done, so the people a
model names are told that a change came through this program. A report the Mycelium does not take is logged
and never fails the tool.

### Prompts

A class implementing `IPromptSource`, registered as a service, answers the prompts it has for a caller at the
moment a chat application asks, so a prompt can depend on what the caller's model holds.
