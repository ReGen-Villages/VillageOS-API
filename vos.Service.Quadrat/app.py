"""Quadrat — the VillageOS raster reader, a managed microservice and pipeline node (FastAPI).

The contract — arguments, inbound JWT validation, registration, the four endpoints, and the pipeline node's
envelope, answer and manifest (docs/SERVICES.md §16.2) — is implemented once in vos.Service.Python.Shared. This
file is what Quadrat adds to it: the operations over the quadrat package, and the ports they read and write.

Run:  .venv/bin/python app.py --port=5110 --myceliumUrl=https://localhost:7243
"""

from __future__ import annotations

import sys

from quadrat import Refusal, Sources, bbox_zone, decoded_tile, describe, sample_point, zonal_stats
from vos_service import Config, NodeContext, NodeService, Port, ServiceHost, parse_args

SERVICE_NAME = "Quadrat"

config = parse_args(sys.argv[1:]) or Config()
sources = Sources.from_args(sys.argv[1:])


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
    Port.input("raster", "string", required=True),
    Port.input("tile", "object"),
    Port.input("lon", "number"),
    Port.input("lat", "number"),
    Port.input("bbox", "array"),
    Port.input("geometry", "object"),
    Port.output("result", "object"),
]


def read_tile(tile) -> tuple[int, int, int]:
    coordinates = [tile.get(axis) if isinstance(tile, dict) else None for axis in ("z", "x", "y")]
    if not all(isinstance(value, int) and not isinstance(value, bool) for value in coordinates):
        raise Refusal("missing_input", "the 'tile' input is {\"z\": n, \"x\": n, \"y\": n}, three whole numbers")
    return tuple(coordinates)


def run_operation(params: dict, inputs: dict) -> dict:
    operation = OPERATIONS.get(params.get("operation"))
    if operation is None:
        raise Refusal("unknown_operation", f"params.operation is one of {', '.join(OPERATIONS)}; got {params.get('operation')!r}")
    if inputs.get("raster") is not None:
        inputs = {**inputs, "raster": sources.permit(inputs["raster"])}
    if inputs.get("tile") is None:
        if params.get("encoding") is not None:
            raise Refusal("encoding_without_tile", "params.encoding says how a terrain tile holds elevation, and no 'tile' input says which tile the raster is")
        return operation(params, inputs)
    z, x, y = read_tile(inputs["tile"])
    with decoded_tile(required(inputs, "raster"), z, x, y, params.get("encoding")) as decoded:
        return operation(params, {**inputs, "raster": decoded})


def execute(context: NodeContext) -> dict:
    return {"result": run_operation(context.params, context.inputs)}


node = NodeService(execute, PORTS)
host = ServiceHost(SERVICE_NAME, config, node.handle, title="VillageOS Quadrat Raster Reader")
node.mount(host)
app = host.app


def main() -> None:
    host.run("app.py")


if __name__ == "__main__":
    main()
