from .classes import classify, histogram
from .limits import Limits
from .metadata import describe
from .point import RESAMPLINGS, sample_point
from .refusal import Refusal
from .remote import confirm_reachable, is_address
from .sources import Sources
from .terrain import ENCODINGS, decoded_tile
from .zonal import zonal_stats
from .zone import bbox_zone


def setting(params, name, default=None):
    """A node declared from the manifest carries every setting, empty until someone sets it; empty is not set."""
    value = params.get(name)
    return default if value is None or value == "" else value


def required(inputs, name):
    if inputs.get(name) is None:
        raise Refusal("missing_input", f"the {name!r} input is required")
    return inputs[name]


def metadata(params, inputs, limits):
    return describe(required(inputs, "raster"))


def point(params, inputs, limits):
    raster = required(inputs, "raster")
    return sample_point(raster, required(inputs, "lon"), required(inputs, "lat"), setting(params, "resampling", "nearest"))


def zone(inputs):
    if inputs.get("bbox") is not None and inputs.get("geometry") is not None:
        raise Refusal("ambiguous_zone", "give the zone as a 'bbox' or as a 'geometry', not both")
    if inputs.get("bbox") is not None:
        return bbox_zone(*inputs["bbox"])
    if inputs.get("geometry") is not None:
        return inputs["geometry"]
    raise Refusal("missing_input", "a zone is required: give a 'bbox' or a 'geometry'")


def zonal(params, inputs, limits):
    return zonal_stats(required(inputs, "raster"), zone(inputs), limits.max_cells)


def histogram_of(params, inputs, limits):
    return histogram(required(inputs, "raster"), zone(inputs), limits.max_cells)


def classification(params, inputs, limits):
    raster, classes = required(inputs, "raster"), required(inputs, "classes")
    return classify(raster, zone(inputs), classes, limits.max_cells)


OPERATIONS = {"metadata": metadata, "point": point, "zonal": zonal, "histogram": histogram_of, "classify": classification}


def read_tile(tile):
    coordinates = [tile.get(axis) if isinstance(tile, dict) else None for axis in ("z", "x", "y")]
    if not all(isinstance(value, int) and not isinstance(value, bool) for value in coordinates):
        raise Refusal("missing_input", "the 'tile' input is {\"z\": n, \"x\": n, \"y\": n}, three whole numbers")
    return tuple(coordinates)


def run_operation(params: dict, inputs: dict, sources: Sources, limits: Limits) -> dict:
    """One request, start to finish. It takes its sources and limits as arguments so it can run in a worker process."""
    operation = OPERATIONS.get(setting(params, "operation"))
    if operation is None:
        raise Refusal("unknown_operation", f"params.operation is one of {', '.join(OPERATIONS)}; got {params.get('operation')!r}")
    if inputs.get("raster") is not None:
        inputs = {**inputs, "raster": sources.permit(inputs["raster"])}
        if is_address(inputs["raster"]):
            confirm_reachable(inputs["raster"])
    if inputs.get("tile") is None:
        if setting(params, "encoding") is not None:
            raise Refusal("encoding_without_tile", "params.encoding says how a terrain tile holds elevation, and no 'tile' input says which tile the raster is")
        return operation(params, inputs, limits)
    z, x, y = read_tile(inputs["tile"])
    with decoded_tile(required(inputs, "raster"), z, x, y, setting(params, "encoding"), limits.max_cells) as decoded:
        return operation(params, {**inputs, "raster": decoded}, limits)
