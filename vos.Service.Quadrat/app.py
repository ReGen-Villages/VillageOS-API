"""Quadrat — the VillageOS raster reader, a managed microservice and pipeline node (FastAPI).

The contract — arguments, inbound JWT validation, registration, the four endpoints — is implemented once
in vos.Service.Python.Shared. This file is what Quadrat adds to it: the node envelope
(docs/SERVICES.md §16.2) mapped onto the quadrat package, and the manifest that declares its ports.

Run:  .venv/bin/python app.py --port=5110 --myceliumUrl=https://localhost:7243
"""

from __future__ import annotations

import asyncio
import sys

from fastapi import Request
from fastapi.responses import JSONResponse

from quadrat import Refusal, bbox_zone, describe, sample_point, zonal_stats
from vos_service import Config, ServiceHost, parse_args

SERVICE_NAME = "Quadrat"

config = parse_args(sys.argv[1:]) or Config()


class NotANodeEnvelope(Exception):
    """A body Quadrat does not read: it answers only pipeline nodes."""


def required(inputs: dict, name: str):
    if inputs.get(name) is None:
        raise Refusal("missing_input", f"the {name!r} input is required")
    return inputs[name]


def metadata(params: dict, inputs: dict) -> dict:
    return describe(required(inputs, "raster"))


def point(params: dict, inputs: dict) -> dict:
    raster = required(inputs, "raster")
    return sample_point(raster, required(inputs, "lon"), required(inputs, "lat"), params.get("resampling", "nearest"))


def zonal(params: dict, inputs: dict) -> dict:
    raster = required(inputs, "raster")
    if inputs.get("bbox") is not None and inputs.get("geometry") is not None:
        raise Refusal("ambiguous_zone", "give the zone as a 'bbox' or as a 'geometry', not both")
    if inputs.get("bbox") is not None:
        return zonal_stats(raster, bbox_zone(*inputs["bbox"]))
    if inputs.get("geometry") is not None:
        return zonal_stats(raster, inputs["geometry"])
    raise Refusal("missing_input", "a zone is required: give a 'bbox' or a 'geometry'")


OPERATIONS = {"metadata": metadata, "point": point, "zonal": zonal}

PORTS = [
    {"portName": "raster", "direction": "in", "type": "string", "required": True},
    {"portName": "lon", "direction": "in", "type": "number", "required": False},
    {"portName": "lat", "direction": "in", "type": "number", "required": False},
    {"portName": "bbox", "direction": "in", "type": "array", "required": False},
    {"portName": "geometry", "direction": "in", "type": "object", "required": False},
    {"portName": "result", "direction": "out", "type": "object", "required": False},
]


def read_envelope(payload: dict) -> tuple[dict, dict]:
    if not isinstance(payload, dict) or "runId" not in payload or "nodeId" not in payload:
        raise NotANodeEnvelope("Quadrat answers pipeline nodes: a request carrying both runId and nodeId, with params and inputs")
    return payload.get("params") or {}, payload.get("inputs") or {}


def run_operation(params: dict, inputs: dict) -> dict:
    operation = OPERATIONS.get(params.get("operation"))
    if operation is None:
        raise Refusal("unknown_operation", f"params.operation is one of {', '.join(OPERATIONS)}; got {params.get('operation')!r}")
    return operation(params, inputs)


async def handle_node(payload: dict, request_number: int) -> dict:
    params, inputs = read_envelope(payload)
    try:
        result = await asyncio.to_thread(run_operation, params, inputs)
    except Refusal as refusal:
        return {"success": False, "outputs": {}, "error": str(refusal)}
    return {"success": True, "outputs": {"result": result}, "error": None}


host = ServiceHost(SERVICE_NAME, config, handle_node, title="VillageOS Quadrat Raster Reader")
app = host.app


@app.exception_handler(NotANodeEnvelope)
async def refuse_a_body_that_is_not_a_node(_: Request, refused: NotANodeEnvelope) -> JSONResponse:
    return JSONResponse({"error": str(refused)}, status_code=400)


@app.get("/manifest")
def manifest() -> dict:
    return {"service": SERVICE_NAME, "ports": PORTS}


def main() -> None:
    host.run("app.py")


if __name__ == "__main__":
    main()
