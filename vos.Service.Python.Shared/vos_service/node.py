from __future__ import annotations

import asyncio
import inspect
import logging
import uuid
from collections.abc import Awaitable, Callable, Sequence
from dataclasses import dataclass, field

from fastapi import Depends, Request
from fastapi.responses import JSONResponse

logger = logging.getLogger("vos_service")

RESULT_SUCCESS = "success"
RESULT_FAIL = "fail"


@dataclass(frozen=True)
class Port:
    """A port a node advertises: the shape of the model's Port archetype, so a manifest can declare it."""

    port_name: str
    direction: str
    type: str
    required: bool = False

    @classmethod
    def input(cls, port_name: str, type: str, required: bool = False) -> Port:
        return cls(port_name, "in", type, required)

    @classmethod
    def output(cls, port_name: str, type: str) -> Port:
        return cls(port_name, "out", type)

    def to_json(self) -> dict:
        return {"portName": self.port_name, "direction": self.direction, "type": self.type, "required": self.required}


@dataclass(frozen=True)
class Setting:
    """A value a node carries as a property and the service reads from `params`; the manifest lists it so a model can declare the node."""

    name: str
    required: bool = False
    values: Sequence | None = None
    default: object = None

    def to_json(self) -> dict:
        described = {"name": self.name, "required": self.required}
        if self.values is not None:
            described["values"] = list(self.values)
        if self.default is not None:
            described["default"] = self.default
        return described


@dataclass(frozen=True)
class NodeContext:
    """One run of a node: who is asking, the node's own settings, and its inputs with every graph reference already read."""

    run_id: uuid.UUID
    node_id: uuid.UUID
    index: int | None
    rehearsal: bool
    params: dict = field(default_factory=dict)
    inputs: dict = field(default_factory=dict)

    def input(self, port_name: str):
        return self.inputs.get(port_name)

    def param(self, name: str):
        return self.params.get(name)


class NotANodeEnvelope(Exception):
    """A body that is not a pipeline node's request, which a node answers with 400."""


class _Malformed(ValueError):
    pass


Execute = Callable[[NodeContext], "dict | None | Awaitable[dict | None]"]


class NodeService:
    """A service's answer to a pipeline node, so it writes only the operation.

    `execute(context)` returns the outputs by output-port name. It may be a plain function, which runs on a
    worker thread so a slow read does not hold up `/health`, or a coroutine function. Anything it raises is a
    `fail` answer carrying the message, never a server error. Hand `handle` to a `ServiceHost`, then
    `mount` the node on that host.

    `max_concurrent` limits how many runs are inside the operation at once. A run over the limit waits for a
    place without holding a thread, so `/health` keeps answering, and the limit bounds what the operations can
    use between them.
    """

    def __init__(
        self, execute: Execute, ports: Sequence[Port], *, rehearses: bool = False, settings: Sequence[Setting] = (),
        max_concurrent: int | None = None,
    ):
        if max_concurrent is not None and max_concurrent < 1:
            raise ValueError(f"max_concurrent is a positive number of runs, or None for no limit; got {max_concurrent}")
        self._run = execute
        self.max_concurrent = max_concurrent
        self._places = asyncio.Semaphore(max_concurrent) if max_concurrent is not None else None
        self.ports = list(ports)
        self.rehearses = rehearses
        self.settings = list(settings)
        self.service_name = "This service"
        self._mycelium = None

    def mount(self, host) -> None:
        """Serve `/manifest` on the host, behind the same token as `/handle`, and answer a body that is no node's request with 400."""
        self._mycelium = host.mycelium
        self.service_name = host.service_name

        @host.app.get("/manifest")
        def manifest(_: None = Depends(host.verify_request)) -> dict:
            return self.manifest()

        @host.app.exception_handler(NotANodeEnvelope)
        async def refuse(_: Request, refused: NotANodeEnvelope) -> JSONResponse:
            return JSONResponse({"error": str(refused)}, status_code=400)

    def manifest(self) -> dict:
        return {
            "service": self.service_name,
            "rehearses": self.rehearses,
            "ports": [port.to_json() for port in self.ports],
            "settings": [setting.to_json() for setting in self.settings],
        }

    async def handle(self, payload: dict, request_number: int) -> dict:
        if not isinstance(payload, dict) or "runId" not in payload or "nodeId" not in payload:
            raise NotANodeEnvelope(f"{self.service_name} answers pipeline nodes: a request carrying both runId and nodeId, with params and inputs")
        try:
            run_id, node_id, index, rehearsal = _identity(payload)
        except _Malformed as malformed:
            return _fail(f"Malformed node envelope: {malformed}")
        try:
            params = payload["params"] if isinstance(payload.get("params"), dict) else {}
            raw_inputs = payload["inputs"] if isinstance(payload.get("inputs"), dict) else {}
            inputs = {port: await self._resolve(value) for port, value in raw_inputs.items()}
            outputs = await self._execute(NodeContext(run_id, node_id, index, rehearsal, params, inputs))
            if outputs is None:
                outputs = {}
            if not isinstance(outputs, dict):
                raise TypeError("a node's outputs are an object keyed by output port name")
            return {"result": RESULT_SUCCESS, "outputs": outputs, "error": None}
        except Exception as failed:
            logger.warning("node %s (run %s) failed: %s", node_id, run_id, failed)
            return _fail(str(failed) or type(failed).__name__)

    async def _execute(self, context: NodeContext):
        if self._places is None:
            return await self._call(context)
        async with self._places:
            return await self._call(context)

    async def _call(self, context: NodeContext):
        if inspect.iscoroutinefunction(self._run):
            return await self._run(context)
        outcome = await asyncio.to_thread(self._run, context)
        return await outcome if inspect.isawaitable(outcome) else outcome

    async def _resolve(self, value):
        reference = _reference(value)
        if reference is None:
            return value
        thing_id, name = reference
        if self._mycelium is None:
            raise RuntimeError("a graph reference cannot be read until the node is mounted on its host")
        try:
            properties = await self._mycelium.thing_properties(thing_id)
        except Exception as unreadable:
            raise RuntimeError(f"Failed to resolve input ref {thing_id}.{name}: {unreadable}") from unreadable
        if isinstance(properties, dict):
            for key, stored in properties.items():
                if key.lower() == name.lower():
                    return _value_of(stored)
        raise LookupError(f"Property '{name}' not found on thing {thing_id}")


def _identity(payload: dict) -> tuple[uuid.UUID, uuid.UUID, int | None, bool]:
    index = payload.get("index")
    if index is not None and (isinstance(index, bool) or not isinstance(index, int)):
        raise _Malformed("index is a whole number when it is sent")
    rehearsal = payload.get("rehearsal")
    if rehearsal is not None and not isinstance(rehearsal, bool):
        raise _Malformed("rehearsal is true or false when it is sent")
    return _guid(payload, "runId"), _guid(payload, "nodeId"), index, rehearsal is True


def _guid(payload: dict, name: str) -> uuid.UUID:
    value = payload[name]
    try:
        if not isinstance(value, str):
            raise ValueError
        return uuid.UUID(value)
    except ValueError:
        raise _Malformed(f"{name} is not a GUID") from None


def _reference(value) -> tuple[uuid.UUID, str] | None:
    """The Thing and property an input points at, if it is `{"ref": {"thingId", "property"}}`; any other value is a literal."""
    ref = value.get("ref") if isinstance(value, dict) else None
    if not isinstance(ref, dict) or not isinstance(ref.get("thingId"), str) or not isinstance(ref.get("property"), str):
        return None
    try:
        return uuid.UUID(ref["thingId"]), ref["property"]
    except ValueError:
        return None


def _value_of(stored):
    """The platform answers each property as an object holding its `Value`; anything else is the value."""
    if isinstance(stored, dict):
        for key, value in stored.items():
            if key.lower() == "value":
                return value
    return stored


def _fail(message: str) -> dict:
    return {"result": RESULT_FAIL, "outputs": {}, "error": message}
