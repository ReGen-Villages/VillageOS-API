"""The playground content: the service catalog and the example pipelines (DAGs).

This is data, not machinery — `generate.py` turns it into VOS Things + Relationships. Add a service to
`SERVICES` or a DAG to `PIPELINES` and it appears in the Trellis Pipeline page (services in the palette,
pipelines in the Load dropdown) the next time the generator runs.

Port `type` is a wire-compatibility tag: an empty type or "any" is a wildcard, so "any" ports connect to
anything. A port with `collection` true drives fan-out on the Pipeline page (one run item per element).
"""

# Each service becomes a palette entry: a PlatformServiceConnection (carrying the Subdomain a run dispatches
# to) bound to a Service that declares the ports. `subdomain` values match real Managed Microservices where
# one exists (echo, water-reserve, energy-balance, metabolism, model-bridge), so a saved pipeline can also be
# Run against a live platform; the rest are dispatch-only stand-ins that still exercise the editor end to end.
#
# port tuple = (portName, direction, type, required, collection)
def _in(name, type="any", required=False, collection=False):
    return (name, "in", type, required, collection)


def _out(name, type="any"):
    return (name, "out", type, False, False)


SERVICES = [
    # ---- sources (no required inputs) --------------------------------------------------------------
    {"key": "generate", "label": "Generate Text", "subdomain": "generate",
     "ports": [_out("echo", "string")]},
    {"key": "timer", "label": "Timer Tick", "subdomain": "timer",
     "ports": [_out("tick", "number")]},
    {"key": "random", "label": "Random Number", "subdomain": "random",
     "ports": [_out("value", "number")]},
    {"key": "site-params", "label": "Site Parameters", "subdomain": "site-params",
     "ports": [_out("population", "number"), _out("area", "number"), _out("households", "number")]},

    # ---- text / data transforms --------------------------------------------------------------------
    {"key": "echo", "label": "Echo", "subdomain": "echo",
     "ports": [_in("message", "string", required=True), _out("echo", "string")]},
    {"key": "uppercase", "label": "Uppercase", "subdomain": "uppercase",
     "ports": [_in("text", "string", required=True), _out("text", "string")]},
    {"key": "reverse", "label": "Reverse", "subdomain": "reverse",
     "ports": [_in("text", "string", required=True), _out("text", "string")]},
    {"key": "merge", "label": "JSON Merge", "subdomain": "merge",
     "ports": [_in("in", "any", required=True), _out("out", "any")]},
    {"key": "format-report", "label": "Format Report", "subdomain": "format-report",
     "ports": [_in("data", "any", required=True), _out("text", "string")]},

    # ---- arithmetic --------------------------------------------------------------------------------
    {"key": "multiply", "label": "Multiply", "subdomain": "multiply",
     "ports": [_in("value", "number", required=True), _in("factor", "number"), _out("result", "number")]},
    {"key": "sum", "label": "Sum", "subdomain": "sum",
     "ports": [_in("a", "number", required=True), _in("b", "number", required=True), _out("total", "number")]},

    # ---- site-analysis domains (match live microservices) ------------------------------------------
    {"key": "water-reserve", "label": "Water Reserve", "subdomain": "water-reserve",
     "ports": [_in("population", "number", required=True),
               _in("perCapitaConsumptionM3", "number", required=True),
               _in("storageCapacityM3", "number", required=True),
               _out("daysOfSupply", "number")]},
    {"key": "energy-balance", "label": "Energy Balance", "subdomain": "energy-balance",
     "ports": [_in("solarPvAreaM2", "number", required=True),
               _in("solarResourceKwhPerM2PerYear", "number", required=True),
               _in("moduleEfficiency", "number", required=True),
               _in("performanceRatio", "number", required=True),
               _in("otherGenerationMwhPerYear", "number", required=True),
               _in("annualConsumptionMwhPerYear", "number", required=True),
               _out("pctOfConsumption", "number")]},
    {"key": "metabolism", "label": "Metabolism", "subdomain": "metabolism",
     "ports": [_in("intakeKcal", "number", required=True),
               _in("populationCount", "number", required=True),
               _out("balanceKcal", "number")]},
    {"key": "model-bridge", "label": "Model Bridge", "subdomain": "model-bridge",
     "ports": [_in("query", "string", required=True), _out("result", "any")]},

    # ---- fan-out over a collection -----------------------------------------------------------------
    {"key": "score", "label": "Scorer", "subdomain": "score",
     "ports": [_in("item", "string", required=True, collection=True),
               _in("weight", "number"), _out("score", "string")]},
    {"key": "enrich", "label": "Batch Enrich", "subdomain": "enrich",
     "ports": [_in("record", "any", required=True, collection=True), _out("enriched", "any")]},

    # ---- sink --------------------------------------------------------------------------------------
    {"key": "publish", "label": "Publish", "subdomain": "publish",
     "ports": [_in("payload", "any", required=True)]},
]


# A pipeline node is either a service node ("service": <catalog key>) or a boundary node
# ("input"/"output": [port specs]). Node "at" is the canvas grid cell (column, row) — the generator turns it
# into x/y. "params" binds an input port to a run-parameter key (Params bar on the Pipeline page); "onItemError"
# = "continue" collects partial fan-out results instead of failing the run.
#
# Boundary port spec = (portName, type[, required]).
def col(c, r=0):
    return {"col": c, "row": r}


PIPELINES = [
    # 1. The canonical linear demo, plus a seeded completed run so the History panel and node status rings
    #    light up with no live Phloem.
    {
        "name": "Hello Echo",
        "nodes": [
            {"key": "gen", "service": "generate", "at": col(0)},
            {"key": "echo", "service": "echo", "at": col(1)},
        ],
        "wires": [{"from": "gen", "fromPort": "echo", "to": "echo", "toPort": "message"}],
        "runs": [
            {"status": "succeeded", "startedUtc": "2026-07-20T09:15:00Z",
             "nodes": [{"node": "gen", "status": "succeeded"}, {"node": "echo", "status": "succeeded"}]},
            {"status": "failed", "startedUtc": "2026-07-20T09:02:00Z",
             "nodes": [{"node": "gen", "status": "succeeded"}, {"node": "echo", "status": "failed"}]},
        ],
    },

    # 2. Boundary I/O: the Input node's `seed` output is filled from the run param `seed`; the value wired into
    #    the Output node becomes the run result.
    {
        "name": "Boundary I/O Echo",
        "nodes": [
            {"key": "in", "input": [("seed", "string")], "at": col(0)},
            {"key": "echo", "service": "echo", "at": col(1)},
            {"key": "out", "output": [("result", "string", True)], "at": col(2)},
        ],
        "wires": [
            {"from": "in", "fromPort": "seed", "to": "echo", "toPort": "message"},
            {"from": "echo", "fromPort": "echo", "to": "out", "toPort": "result"},
        ],
    },

    # 3. A multi-stage text transform, driven from a run parameter.
    {
        "name": "Text Studio",
        "nodes": [
            {"key": "in", "input": [("text", "string")], "at": col(0)},
            {"key": "upper", "service": "uppercase", "at": col(1)},
            {"key": "rev", "service": "reverse", "at": col(2)},
            {"key": "out", "output": [("text", "string", True)], "at": col(3)},
        ],
        "wires": [
            {"from": "in", "fromPort": "text", "to": "upper", "toPort": "text"},
            {"from": "upper", "fromPort": "text", "to": "rev", "toPort": "text"},
            {"from": "rev", "fromPort": "text", "to": "out", "toPort": "text"},
        ],
    },

    # 4. Two sources land at different to-paths of one input, so JSON Merge deep-merges them (#5874).
    {
        "name": "Field Merge",
        "nodes": [
            {"key": "a", "service": "generate", "at": col(0, 0)},
            {"key": "b", "service": "random", "at": col(0, 1)},
            {"key": "merge", "service": "merge", "at": col(1)},
            {"key": "out", "output": [("result", "any", True)], "at": col(2)},
        ],
        "wires": [
            {"from": "a", "fromPort": "echo", "to": "merge", "toPort": "in", "toPath": "greeting"},
            {"from": "b", "fromPort": "value", "to": "merge", "toPort": "in", "toPath": "roll"},
            {"from": "merge", "fromPort": "out", "to": "out", "toPort": "result"},
        ],
    },

    # 5. A JSONata transform reshapes the upstream output on the wire before it reaches the input (#5875).
    {
        "name": "Wire Transform",
        "nodes": [
            {"key": "gen", "service": "generate", "at": col(0)},
            {"key": "echo", "service": "echo", "at": col(1)},
        ],
        "wires": [
            {"from": "gen", "fromPort": "echo", "to": "echo", "toPort": "message",
             "transform": '{"message": "shout: " & $}'},
        ],
    },

    # 6. A wider diamond: one source fans to two transforms that merge, then a report — exercises canvas layout
    #    and multi-input merge in one DAG.
    {
        "name": "Diamond Fan",
        "nodes": [
            {"key": "gen", "service": "generate", "at": col(0, 1)},
            {"key": "upper", "service": "uppercase", "at": col(1, 0)},
            {"key": "rev", "service": "reverse", "at": col(1, 2)},
            {"key": "merge", "service": "merge", "at": col(2, 1)},
            {"key": "report", "service": "format-report", "at": col(3, 1)},
            {"key": "out", "output": [("report", "string", True)], "at": col(4, 1)},
        ],
        "wires": [
            {"from": "gen", "fromPort": "echo", "to": "upper", "toPort": "text"},
            {"from": "gen", "fromPort": "echo", "to": "rev", "toPort": "text"},
            {"from": "upper", "fromPort": "text", "to": "merge", "toPort": "in", "toPath": "upper"},
            {"from": "rev", "fromPort": "text", "to": "merge", "toPort": "in", "toPath": "lower"},
            {"from": "merge", "fromPort": "out", "to": "report", "toPort": "data"},
            {"from": "report", "fromPort": "text", "to": "out", "toPort": "report"},
        ],
    },

    # 7. Fan-out: the Scorer's `item` input is a collection, so a list run param spreads into one run item each;
    #    onItemError=continue collects partial results. A seeded run shows fan-out progress in the panel.
    {
        "name": "Fan-out Scoring",
        "nodes": [
            {"key": "score", "service": "score", "at": col(0),
             "params": {"item": "items", "weight": "weight"}, "onItemError": "continue"},
            {"key": "out", "output": [("scores", "any", True)], "at": col(1)},
        ],
        "wires": [{"from": "score", "fromPort": "score", "to": "out", "toPort": "scores"}],
        "runs": [
            {"status": "partial", "startedUtc": "2026-07-21T14:30:00Z",
             "nodes": [{"node": "score", "status": "partial", "fanout": {"done": 4, "total": 5}},
                       {"node": "out", "status": "succeeded"}]},
        ],
    },

    # 8. Water self-sufficiency: all three inputs param-bound, so a run supplies them (#5805).
    {
        "name": "Water Self-Sufficiency",
        "nodes": [
            {"key": "water", "service": "water-reserve", "at": col(0),
             "params": {"population": "population",
                        "perCapitaConsumptionM3": "perCapitaConsumptionM3",
                        "storageCapacityM3": "storageCapacityM3"}},
            {"key": "out", "output": [("daysOfSupply", "number", True)], "at": col(1)},
        ],
        "wires": [{"from": "water", "fromPort": "daysOfSupply", "to": "out", "toPort": "daysOfSupply"}],
    },

    # 9. Energy self-sufficiency: every input param-bound (#5806).
    {
        "name": "Energy Self-Sufficiency",
        "nodes": [
            {"key": "energy", "service": "energy-balance", "at": col(0),
             "params": {"solarPvAreaM2": "solarPvAreaM2",
                        "solarResourceKwhPerM2PerYear": "solarResourceKwhPerM2PerYear",
                        "moduleEfficiency": "moduleEfficiency",
                        "performanceRatio": "performanceRatio",
                        "otherGenerationMwhPerYear": "otherGenerationMwhPerYear",
                        "annualConsumptionMwhPerYear": "annualConsumptionMwhPerYear"}},
            {"key": "out", "output": [("pctOfConsumption", "number", True)], "at": col(1)},
        ],
        "wires": [{"from": "energy", "fromPort": "pctOfConsumption", "to": "out", "toPort": "pctOfConsumption"}],
    },

    # 10. Capstone: a source supplies one wired input, two others are param-bound, two analyses run in parallel,
    #     merge into a report, and end at a sink — sources + wires + params + merge + sink in one DAG.
    {
        "name": "Site Analysis (Combined)",
        "nodes": [
            {"key": "site", "service": "site-params", "at": col(0, 1)},
            {"key": "water", "service": "water-reserve", "at": col(1, 0),
             "params": {"perCapitaConsumptionM3": "perCapitaConsumptionM3",
                        "storageCapacityM3": "storageCapacityM3"}},
            {"key": "energy", "service": "energy-balance", "at": col(1, 2),
             "params": {"solarPvAreaM2": "solarPvAreaM2",
                        "solarResourceKwhPerM2PerYear": "solarResourceKwhPerM2PerYear",
                        "moduleEfficiency": "moduleEfficiency",
                        "performanceRatio": "performanceRatio",
                        "otherGenerationMwhPerYear": "otherGenerationMwhPerYear",
                        "annualConsumptionMwhPerYear": "annualConsumptionMwhPerYear"}},
            {"key": "merge", "service": "merge", "at": col(2, 1)},
            {"key": "report", "service": "format-report", "at": col(3, 1)},
            {"key": "publish", "service": "publish", "at": col(4, 1)},
        ],
        "wires": [
            {"from": "site", "fromPort": "population", "to": "water", "toPort": "population"},
            {"from": "water", "fromPort": "daysOfSupply", "to": "merge", "toPort": "in", "toPath": "water.daysOfSupply"},
            {"from": "energy", "fromPort": "pctOfConsumption", "to": "merge", "toPort": "in", "toPath": "energy.pctOfConsumption"},
            {"from": "merge", "fromPort": "out", "to": "report", "toPort": "data"},
            {"from": "report", "fromPort": "text", "to": "publish", "toPort": "payload"},
        ],
    },

    # 11. A numeric chain mixing wired and param-bound inputs on the same node.
    {
        "name": "Number Cruncher",
        "nodes": [
            {"key": "rand", "service": "random", "at": col(0)},
            {"key": "mul", "service": "multiply", "at": col(1), "params": {"factor": "factor"}},
            {"key": "sum", "service": "sum", "at": col(2), "params": {"b": "offset"}},
            {"key": "out", "output": [("total", "number", True)], "at": col(3)},
        ],
        "wires": [
            {"from": "rand", "fromPort": "value", "to": "mul", "toPort": "value"},
            {"from": "mul", "fromPort": "result", "to": "sum", "toPort": "a"},
            {"from": "sum", "fromPort": "total", "to": "out", "toPort": "total"},
        ],
    },

    # 12. Fan-out into a sink: a list of records is enriched one item at a time, then published.
    {
        "name": "Enrichment Batch",
        "nodes": [
            {"key": "in", "input": [("records", "any")], "at": col(0)},
            {"key": "enrich", "service": "enrich", "at": col(1), "onItemError": "continue"},
            {"key": "publish", "service": "publish", "at": col(2)},
        ],
        "wires": [
            {"from": "in", "fromPort": "records", "to": "enrich", "toPort": "record"},
            {"from": "enrich", "fromPort": "enriched", "to": "publish", "toPort": "payload"},
        ],
    },
]
