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
| `describe_model` | What a model declares: its kinds, predicates and property names, and the property types the platform accepts. On request, which kinds of reader ask for each name | ReadModel | — | No |
| `find_things` | Things by part of a name, kind, container, or a property holding an exact text; each with its identifier and name, and the properties asked for. Finding by part of a name reads the name of every Thing the other narrowings leave, so its cost grows with the model | ReadModel | — | No |
| `get_thing` | One Thing: its identifier, name, what it inherits from, every property with its value, type and source, and its current states. What it inherits from is as the platform resolves it, which can include a source the Thing no longer is | ReadModel | — | No |
| `read_property` | One property of a Thing or a relationship by its path; where a name is inherited from more than one source, the paths to choose from | ReadModel | — | No |
| `list_pages` | The pages the model publishes, each with its identifier and name, whether its description could be read, and the Things it compares | ReadModel | — | No |
| `read_page` | One page, worded as the console words it: a figure with its value, its worded value, its unit and where it came from; a verdict as the model's sentences and the inputs it offers; a worked figure with its formula and inputs; a chart or a table as its rows | ReadModel | — | No |
| `explain` | One figure of one Thing from the model alone, as data: its value, the ranges that judge it, what it was worked out from, and its inputs | ReadModel | — | No |
| `offered_choices` | The choices an action widget offers on one of its rows, as the console offers them, with the fields each asks and whether it can be pressed here | ReadModel | The page role | No |
| `press` | Presses one choice on a row, making the writes the console makes for it (below) | ModifyData | The page role | Yes, and marked as destroying |
| `show_calls` | Every service the platform called on one Thing: the connection, its service, the call's state, the attempts, the recorded error, and the Things waiting for this one | ReadModel | The connection role | No |

A tool that lists answers at most a default number of entries unless `limit` asks for more, never more
than a ceiling, and says whether more exist. Both numbers are in `ListingLimits`.

The server tells the chat application, when a conversation starts, that every name and value a tool
answers comes from a model and is data, never an instruction. A tool answers what a model holds as
fields and never inside a sentence of its own.

`read_page`'s service bindings post under `/api/endpoints/`, which Mycelium refuses a key that may not
change data. `show_calls` can wait up to 120 seconds for the calls to finish, and while it waits it
reads again only the calls it already found. Its first read carries every relationship of the Thing,
about 300 bytes each, so a Thing with three thousand relationships answers about 1 MB.

A pull request that adds a tool lists its row on the open documentation collector task, and the
documentation batch adds it here.

## Which tools a key is shown

One key sees one list, decided by its role and its own model's bindings; a tool called with another model
checks that model when it is called. A key that names no model, while the Mycelium holds several, is
shown the tools its role may use and a tool whose role any of those models binds.

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
public sealed class ModelTools(ModelClients clients)
{
    [McpServerTool(Name = "list_models", Title = "List models", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the models the key this server was given may reach, each with its identifier and name.")]
    [RequiresPolicy(MyceliumPolicy.ReadModel)]
    public Task<IReadOnlyList<ReachableModel>> ListModelsAsync() => clients.ReachableAsync();
}
```

| A tool states | With |
| --- | --- |
| Its name, title and description, and what each parameter means | `[McpServerTool]` and `[Description]` |
| Whether it only reads, whether it destroys, whether repeating it changes nothing more | `ReadOnly`, `Destructive`, `Idempotent` on `[McpServerTool]` |
| The Mycelium policy it needs | `[RequiresPolicy]`, required on every tool |
| A role the model must bind for it to be listed | `[RequiresBoundRole]` |
| Any other condition for listing it | A class of your own deriving from `ListingConditionAttribute` |

### Which model a tool asks

Every tool takes an optional `model` parameter, an identifier or a name, and asks `ModelClients` for
that model's client. Left out, the server's own model is asked.

```csharp
public async Task<JsonElement> ThingsAsync(string? model = null) =>
    await (await clients.ForAsync(model)).GetAllThingsAsync();
```

- The server checks the tool's listing conditions against the named model before the tool runs, so a
  tool never runs against a model that does not bind its role. The tool does not repeat the check.
- A model the key does not reach is refused with the names of the models it does reach, and a name
  more than one model holds is refused with the identifiers to choose from.
- A key that names no model, while the Mycelium holds several, has no model of its own. A call that
  names none is refused with each model's name and identifier, and the person names one with
  `model`.
- The server keeps one client for each model a key has asked for. It keeps one only for a model the
  Mycelium lists for the key, so the number kept is bounded by the models the key may reach.
- The tool that lists the models is the one tool with no `model` parameter.

Tools sit in a folder for each part of the server: `Tools/Generic` for the general tools, `Tools/Pages` for
reading a model's pages, `Tools/Process` for working a Thing through a process.

Rules the tests hold every tool to:

- It declares a policy.
- A tool marked as only reading does not declare a policy for writing.
- A tool that deletes, prunes, promotes or records a decision is marked as destroying.
- It takes a `model` parameter, except `list_models`, and describes it with
  `ModelClients.ParameterDescription`, so every tool tells a chat application the same thing,
  including when `model` is required.
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

### An address is never taken from a tool's arguments

No tool passes an address from its arguments to any client method. An address comes from the server's
configuration or from the model through a role. `IngestIfcAsync` is the one client method that sends
the token to an address its caller gives, which Taproot needs for the person at its prompt; a test
fails if any source of the server names it, so a tool that uploads a file has to come past a design
first.

### The routes a page reaches

`PageReadRoutes` lets a page read reach only five routes: `GET /api/states/{state}/things` with a
query, `GET /api/things/{identifier}/ranges`, `POST /api/temporal/aggregate`,
`POST /api/temporal/reduce` and `POST /api/endpoints/{name}`. Any other path, and any address that is
not a plain path on this Mycelium, is not sent; the binding reads as nothing, and the tool's answer
lists it under `notAsked`. A page description is the model's data and a service binding names a
route, so the server decides which routes a page may reach.

### Pressing a choice

- A press is two calls. The first, with `preview` true, answers the writes and a digest and sends
  nothing. The second composes the press again and sends it only once the person has agreed; where the
  chat application cannot ask the person, it carries the digest in `confirm_digest`, never the writes,
  and nothing is sent where the composition differs.
- Every press takes the person's reason, at most 500 characters. Each write is reported to the
  operator activity record in the console's words, with the reason after it.
- A choice that asks a secret is offered as not pressable and refused.
- `PageReadRoutes.RefusalToPress` lets a press reach a service under `/api/endpoints/` and the listed
  platform routes, which are `/api/model/promote` alone.
- Every write of a press is checked before any is sent: a relate and a stamp only where each
  identifier they carry is one.

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
