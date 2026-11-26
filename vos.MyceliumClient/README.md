# vos.MyceliumClient

The library a program reaches the Mycelium through. Taproot and the MCP server, Hypha (`vos.Hypha`),
both use it, so a call to the Mycelium is written once. Its types are in the namespace
`vos.PlatformClient`.

## What it holds

- `MyceliumClient` — one method per Mycelium route it covers. It exchanges an API key for a token,
  takes a new one before the old one expires, and names the program it runs in to the Mycelium in the
  `X-Vos-Client` header. A refusal is thrown as an `HttpRequestException` whose message carries the
  Mycelium's own reason. A missing or refused key is thrown as `KeyRefusedException`. When the
  Mycelium refuses a key because it names no model while several are open, the exception is a
  `ModelNotNamedException`, whose `Models` lists each model the key may enter as
  `ReachableModel(Id, Name)`; its message is the same.
  `ForModel` answers a client for another model, with the same key and connection and a token of its
  own. `RequestAsync` asks a route by its path, for a route that has no method of its own. The path
  must begin `/api/`, hold no backslash, and take no step up the path, written plainly or encoded; it
  is checked again after the address is built, so the token never goes to a route outside `/api/`
  (`PlatformRoute`). `ReadSelectionAsync` reads a selection once (`POST api/snapshots`). It takes the
  selector as a JSON tree and never as text, so a name the selector holds cannot change what is
  selected.
- `NameResolver` — a Thing by its identifier or its name. A name more than one Thing holds is
  refused, with the identifiers to choose from. `FindThingAsync` asks the Mycelium for the one name;
  `ResolveThingAsync` reads every Thing to find it. The Mycelium reads a comma as separating names, so
  `FindThingAsync` finds a name holding one by reading every Thing.
- `ThingSearch` — Things whose name contains a given text, without regard to case. Every Thing the
  other narrowings leave is read and its name compared, so the cost grows with the model.
- `ThingReads` — one Thing by its identifier or name (`OneAsync`), and what it holds in whole
  (`WholeAsync`): its properties with their sources, what it inherits from, and its current states,
  read together.
- `ModelSnapshot` and `ModelReading` — a model read once, and the Thing a role binds read from it.
- `ConsoleOptions` — the Mycelium's address and the API key. The key is read from `VOS_API_KEY` only,
  never from the command line.
- `PropertyTypeNames`, `Timestamps`, `ServerSentEvents`, `OperatorMessage`, `JsonElementExtensions` —
  small helpers the client and its callers share.

## Calls made at the same time

A token is attached to each request as it leaves, and each model's client holds its own, so calls made
at the same time, for one model or several, write nothing they share.

**A call takes no cancellation, except a stream.** Every request ends after its time limit, so a read
nobody is waiting for costs little. A write that was cut off would leave the person not knowing
whether it happened, so a write that has started completes.

## Adding a route

`MyceliumClient` is a partial class, so a method for one area of routes can go in a file of its own
beside `MyceliumClient.cs`. Follow the pattern of the existing methods: make the call, and pass the
response through `EnsureSuccessCarryingTheReasonAsync` so a refusal keeps its reason.
