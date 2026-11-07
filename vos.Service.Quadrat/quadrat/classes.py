import numpy
import shapely
from exactextract import exact_extract
from pyproj import CRS, Geod, Transformer
from shapely.geometry import mapping

from .refusal import Refusal
from .source import WGS84
from .zonal import zone_window

# A land-cover raster has a handful of codes. A raster with far more distinct values is a measurement (elevation,
# rainfall), and a count of each of its values says nothing a person could use.
MAX_DISTINCT_VALUES = 1000
GEOD = Geod(ellps="WGS84")


def histogram(path, geometry, max_cells=None):
    """The cells of each distinct value inside a zone, with the share of the zone and the ground area each covers.

    A cell the zone only partly covers counts for the part it covers. Cells with no data are reported on their own.
    Areas are each value's share of the zone's cells times the zone's ground area, so cells are taken to be of one
    size across the zone.
    """
    return _measured(path, geometry, max_cells)


def classify(path, geometry, classes, max_cells=None):
    """The same measure grouped by a class table stated in the request: [{"label": ..., "values": [...]}].

    A value no class names is reported as unclassified with its values, never put in a class by guess.
    """
    table = _checked(classes)
    measured = _measured(path, geometry, max_cells)
    covered, area = measured["covered_cells"], measured["area_m2"]
    by_value = {entry["value"]: entry for entry in measured["classes"]}
    named = {value for entry in table for value in entry["values"]}
    grouped = []
    for entry in table:
        cells = sum(by_value[value]["cells"] for value in entry["values"] if value in by_value)
        grouped.append({"label": entry["label"], "cells": cells, **_share(cells, covered, area)})
    leftover = sorted(value for value in by_value if value not in named)
    cells = sum(by_value[value]["cells"] for value in leftover)
    return {
        "classes": grouped,
        "unclassified": {"values": leftover, "cells": cells, **_share(cells, covered, area)},
        "nodata": measured["nodata"],
        "covered_cells": covered,
        "area_m2": area,
    }


def _measured(path, geometry, max_cells):
    with zone_window(path, geometry, max_cells) as (dataset, clipped):
        feature = {"type": "Feature", "properties": {}, "geometry": mapping(clipped)}
        counted = exact_extract(path, feature, ["unique", "frac", "count"], output="geojson")[0]["properties"]
        values = numpy.atleast_1d(counted["unique"])
        if len(values) > MAX_DISTINCT_VALUES:
            raise Refusal(
                "too_many_values",
                f"this zone holds {len(values)} distinct values, and a count per value is for a raster of classes "
                f"(at most {MAX_DISTINCT_VALUES}), not a measurement")
        covered = clipped.area / abs(dataset.res[0] * dataset.res[1])
        valid = float(counted["count"] or 0.0)
        area = _ground_area(clipped, dataset)
        classes = [
            {"value": _plain(value), "cells": valid * float(fraction), **_share(valid * float(fraction), covered, area)}
            for value, fraction in zip(values, numpy.atleast_1d(counted["frac"]))
        ]
        gap = max(0.0, covered - valid)
        return {"classes": classes, "nodata": {"cells": gap, **_share(gap, covered, area)}, "covered_cells": covered, "area_m2": area}


def _share(cells, covered, area):
    fraction = cells / covered if covered else 0.0
    return {"fraction": fraction, "area_m2": fraction * area}


def _plain(value):
    number = value.item() if hasattr(value, "item") else value
    return int(number) if float(number).is_integer() else float(number)


def _ground_area(clipped, dataset):
    """The area of the zone's part on the raster, in square metres on the WGS84 ellipsoid."""
    west, south, east, north = clipped.bounds
    longest = max(east - west, north - south)
    zone = shapely.segmentize(clipped, longest / 100) if longest > 0 else clipped
    to_wgs84 = Transformer.from_crs(CRS.from_wkt(dataset.crs.to_wkt()), WGS84, always_xy=True)
    moved = shapely.transform(zone, lambda coordinates: numpy.column_stack(to_wgs84.transform(coordinates[:, 0], coordinates[:, 1])))
    return abs(GEOD.geometry_area_perimeter(moved)[0])


def _checked(classes):
    unclear = 'classes is a list of {"label": text, "values": [numbers]}, each label once and each value in one class'
    if not isinstance(classes, list) or not classes:
        raise Refusal("invalid_classes", unclear)
    labels, seen = set(), set()
    for entry in classes:
        if not isinstance(entry, dict) or set(entry) != {"label", "values"}:
            raise Refusal("invalid_classes", unclear)
        label, values = entry["label"], entry["values"]
        if not isinstance(label, str) or not label or label in labels:
            raise Refusal("invalid_classes", unclear)
        if not isinstance(values, list) or not values or any(isinstance(v, bool) or not isinstance(v, (int, float)) for v in values):
            raise Refusal("invalid_classes", unclear)
        if seen.intersection(values):
            raise Refusal("invalid_classes", f"{unclear}; {sorted(seen.intersection(values))} is in more than one class")
        labels.add(label)
        seen.update(values)
    return classes
