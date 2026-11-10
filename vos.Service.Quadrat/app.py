"""Quadrat — the VillageOS raster reader, a managed microservice and pipeline node (FastAPI).

The contract — arguments, inbound JWT validation, registration, the four endpoints, and the pipeline node's
envelope, answer and manifest (docs/SERVICES.md §16.2) — is implemented once in vos.Service.Python.Shared. This
file is what Quadrat adds to it: the operations over the quadrat package, and the ports they read and write.

Run:  .venv/bin/python app.py --port=5110 --myceliumUrl=https://localhost:7243
"""

from __future__ import annotations

import sys

from quadrat import Limits, Sources
from quadrat.operations import OPERATIONS, run_operation
from quadrat.point import RESAMPLINGS
from quadrat.pool import ReadPool
from quadrat.terrain import ENCODINGS
from quadrat.tickets import held, names_a_ticket
from vos_service import Config, NodeContext, NodeService, Port, ServiceHost, Setting, parse_args

SERVICE_NAME = "Quadrat"

config = parse_args(sys.argv[1:]) or Config()
sources = Sources.from_args(sys.argv[1:])
limits = Limits.from_args(sys.argv[1:])
pool = ReadPool(limits.max_concurrent_reads)


PORTS = [
    Port.input("raster", "string", required=True),
    Port.input("tile", "object"),
    Port.input("lon", "number"),
    Port.input("lat", "number"),
    Port.input("bbox", "array"),
    Port.input("geometry", "object"),
    Port.input("classes", "array"),
    Port.input("line", "object"),
    Port.input("spacing", "number"),
    Port.input("samples", "number"),
    Port.output("result", "object"),
]


SETTINGS = [
    Setting("operation", required=True, values=list(OPERATIONS)),
    Setting("resampling", values=RESAMPLINGS, default="nearest"),
    Setting("encoding", values=list(ENCODINGS)),
]


async def execute(context: NodeContext) -> dict:
    raster = context.inputs.get("raster")
    if not names_a_ticket(raster):
        return {"result": await pool.run(run_operation, context.params, context.inputs, sources, limits)}
    async with held(host.mycelium, raster, limits.max_ticket_bytes, sources) as (path, readable):
        return {"result": await pool.run(run_operation, context.params, {**context.inputs, "raster": path}, readable, limits)}


node = NodeService(execute, PORTS, rehearses=True, settings=SETTINGS, max_concurrent=limits.max_concurrent_reads)
host = ServiceHost(SERVICE_NAME, config, node.handle, title="VillageOS Quadrat Raster Reader")
node.mount(host)
app = host.app


def main() -> None:
    host.run("app.py")


if __name__ == "__main__":
    main()
