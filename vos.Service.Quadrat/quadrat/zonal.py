import math

import numpy
import shapely
from exactextract import exact_extract
from shapely.geometry import box, mapping, shape

from .refusal import Refusal
from .source import from_wgs84, open_raster

OPERATIONS = ["count", "mean", "min", "max", "median"]
POLYGONS = ("Polygon", "MultiPolygon")


def zonal_stats(path, geometry, max_cells=None):
    if geometry.get("type") not in POLYGONS:
        raise Refusal("unsupported_geometry", f"a zone is a Polygon or MultiPolygon, not {geometry.get('type')!r}")
    wanted = shape(geometry)
    west, south, east, north = wanted.bounds
    if not (-180 <= west and east <= 180 and -90 <= south and north <= 90) or not wanted.is_valid:
        raise Refusal("invalid_geometry", "a zone is a valid polygon in WGS84 degrees (no self-crossing, within -180..180 and -90..90)")
    with open_raster(path) as dataset:
        if dataset.transform.b or dataset.transform.d:
            raise Refusal("rotated_raster", "a raster whose grid is rotated against its axes is not read")
        zone = _into_raster_crs(wanted, dataset)
        clipped = zone.intersection(box(*dataset.bounds))
        if clipped.is_empty or clipped.area == 0:
            raise Refusal("outside_raster", "the zone lies outside the raster")
        if max_cells is not None:
            window = _window_cells(dataset, clipped.bounds)
            if window > max_cells:
                raise Refusal(
                    "too_many_cells",
                    f"this zone needs a window of {window} cells of the raster to be read, and this service reads at most "
                    f"{max_cells} in one request: use a smaller zone or a coarser raster")
        feature = {"type": "Feature", "properties": {}, "geometry": mapping(clipped)}
        counted = exact_extract(path, feature, OPERATIONS, output="geojson")[0]["properties"]
        covered_cells = clipped.area / abs(dataset.res[0] * dataset.res[1])
        count = _number(counted["count"]) or 0.0
        return {
            "count": count,
            "mean": _number(counted["mean"]),
            "min": _number(counted["min"]),
            "max": _number(counted["max"]),
            "median": _number(counted["median"]),
            "covered_cells": covered_cells,
            "nodata_cells": max(0.0, covered_cells - count),
        }


def _window_cells(dataset, bounds):
    """The cells of the grid that the bounds touch, which is what a read of the zone must load, not the zone's area."""
    west, south, east, north = bounds
    transform = dataset.transform
    columns = (west - transform.c) / abs(transform.a), (east - transform.c) / abs(transform.a)
    rows = (north - transform.f) / transform.e, (south - transform.f) / transform.e
    width = math.ceil(max(columns) - 1e-6) - math.floor(min(columns) + 1e-6)
    height = math.ceil(max(rows) - 1e-6) - math.floor(min(rows) + 1e-6)
    return max(width, 1) * max(height, 1)


def _into_raster_crs(zone, dataset):
    # An edge straight in degrees is not straight in another projection, so edges are cut short before they move.
    west, south, east, north = zone.bounds
    longest = max(east - west, north - south)
    if longest > 0:
        zone = shapely.segmentize(zone, longest / 100)
    to_raster = from_wgs84(dataset)
    return shapely.transform(zone, lambda coordinates: numpy.column_stack(to_raster.transform(coordinates[:, 0], coordinates[:, 1])))


def _number(value):
    if value is None:
        return None
    number = float(value)
    return None if math.isnan(number) else number
