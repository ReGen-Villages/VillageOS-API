# vos.Service.Python.Shared — the managed-microservice contract, for Python

The plumbing every Python VillageOS service needs, so a service writes its handler and nothing else. It is the
Python counterpart of the C# [`vos.Auth.Shared`](../vos.Auth.Shared) and
[`vos.Service.Shared`](../vos.Service.Shared) libraries. The contract it implements is in
[docs/SERVICE_AUTHORING.md](../docs/SERVICE_AUTHORING.md) and [docs/SERVICE_CONTRACT.md](../docs/SERVICE_CONTRACT.md).

The importable package is `vos_service`. Nothing domain-specific belongs here: a helper moves in when a second
service needs it.

## Use it from a service

A service installs it from the folder, as a path dependency, from its own `requirements.txt`:

```
-e ../vos.Service.Python.Shared
```

Nothing is published to a package index.

## Answer pipeline nodes

A service that is a pipeline node gives `NodeService` its operation and nothing else of the node contract
([docs/SERVICES.md §16.2](../docs/SERVICES.md)). It is the counterpart of the C# `DagNodeService`.

```python
from vos_service import Config, NodeService, Port, ServiceHost, Setting, parse_args

def echo(context):                                   # a plain function runs on a worker thread; an async one on the loop
    return {"echo": context.input("message")}        # the outputs, by output-port name

node = NodeService(
    echo,
    [Port.input("message", "string", required=True), Port.output("echo", "string")],
    rehearses=True,                                  # writes nothing but its answer, so a rehearsal may call it
    settings=[Setting("mode", values=["loud", "quiet"], default="quiet")],
)
host = ServiceHost("Example", parse_args(sys.argv[1:]) or Config(), node.handle)
node.mount(host)
```

- **The request.** `runId` and `nodeId` (GUIDs), `params`, `inputs`, `index` (the item's place when a node runs once
  per item, otherwise absent) and `rehearsal` (sent only when true) arrive as a `NodeContext`. A body without both
  `runId` and `nodeId` is not a node's request and is refused with `400`. A field that is present but malformed is a
  `fail` answer saying which.
- **The answer.** `{"result": "success", "outputs": {...}, "error": null}`, or `{"result": "fail", "outputs": {},
  "error": "<message>"}`. Phloem reads `result` and fails any answer without it, so a service never writes the answer
  itself. Anything the operation raises is a `fail` answer carrying its message, never a server error.
- **Graph references.** An input given as `{"ref": {"thingId": ..., "property": ...}}` is replaced by that
  property's current value before the operation runs, as `DagNodeService` does. A reference that cannot be read, or to a
  property the Thing lacks, fails the run before the operation is called. Any other value, including a malformed
  `ref`, is a literal.
- **Limiting runs.** `NodeService(..., max_concurrent=2)` lets at most that many runs be inside the operation at once.
  A run over the limit waits for a place without holding a thread, so `/health` keeps answering, and a failed run
  gives its place back. A service whose operation takes memory in proportion to the request sets it to bound the
  total; with none, every run goes at once.
- **`GET /manifest`.** Served from what the node declares: `{"service", "rehearses", "ports", "settings"}`, the shape
  `tools/pipeline-playground/declare_service.py` reads to declare the service in a seed. It needs the same token as
  `/handle` when a `VerificationKey` is set, as in the C# services.

## Test

```bash
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python -m pytest
```
