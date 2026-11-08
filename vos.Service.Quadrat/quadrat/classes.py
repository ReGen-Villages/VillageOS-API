import math

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
    """The same measure grouped by a class table stated in the request.

    A class names exact values, `{"label": ..., "values": [...]}`, or a range, `{"label": ..., "min": ..., "max": ...}`
    that takes every value from `min` up to but not including `max` (either bound may be left out). A cell no class
    names is reported as unclassified, never put in a class by guess.
    """
    table = _checked(classes)
    if any(entry["values"] is None for entry in table):
        return _classified_by_cell(path, geometry, table, max_cells)
    return _classified_by_value(path, geometry, table, max_cells)


def _classified_by_value(path, geometry, table, max_cells):
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


def _classified_by_cell(path, geometry, table, max_cells):
    """Ranges need each cell's value, not a count per distinct value, so the cells of the zone window are read."""
    with zone_window(path, geometry, max_cells) as (dataset, clipped):
        feature = {"type": "Feature", "properties": {}, "geometry": mapping(clipped)}
        read = exact_extract(path, feature, ["values", "coverage"], output="geojson")[0]["properties"]
        values = numpy.atleast_1d(numpy.asarray(read["values"], dtype="float64"))
        weights = numpy.atleast_1d(numpy.asarray(read["coverage"], dtype="float64"))
        covered = clipped.area / abs(dataset.res[0] * dataset.res[1])
        area = _ground_area(clipped, dataset)
        taken = numpy.zeros(len(values), dtype=bool)
        grouped = []
        for entry in table:
            inside = _inside(entry, values)
            taken |= inside
            cells = float(weights[inside].sum())
            grouped.append({"label": entry["label"], "cells": cells, **_share(cells, covered, area)})
        left = ~taken
        cells = float(weights[left].sum())
        gap = max(0.0, covered - float(weights.sum()))
        return {
            "classes": grouped,
            "unclassified": {
                "cells": cells, **_share(cells, covered, area),
                "min": _plain(values[left].min()) if left.any() else None,
                "max": _plain(values[left].max()) if left.any() else None,
            },
            "nodata": {"cells": gap, **_share(gap, covered, area)},
            "covered_cells": covered,
            "area_m2": area,
        }


def _inside(entry, values):
    if entry["values"] is not None:
        return numpy.isin(values, entry["values"])
    return (values >= entry["min"]) & (values < entry["max"])


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
    unclear = (
        'classes is a list of {"label": text, "values": [numbers]} or {"label": text, "min": number and/or "max": number}, '
        "each label once, no value in more than one class and no ranges that overlap (a range takes min up to, not including, max)"
    )
    if not isinstance(classes, list) or not classes:
        raise Refusal("invalid_classes", unclear)
    table, labels = [], set()
    for entry in classes:
        if not isinstance(entry, dict) or not isinstance(entry.get("label"), str) or not entry["label"] or entry["label"] in labels:
            raise Refusal("invalid_classes", unclear)
        labels.add(entry["label"])
        table.append(_clear(entry, unclear))
    _apart(table, unclear)
    return table


def _clear(entry, unclear):
    keys = set(entry) - {"label"}
    if keys == {"values"}:
        values = entry["values"]
        if not isinstance(values, list) or not values or not all(_is_number(v) for v in values):
            raise Refusal("invalid_classes", unclear)
        return {"label": entry["label"], "values": values}
    if not keys or not keys <= {"min", "max"} or not all(_is_number(entry[key]) for key in keys):
        raise Refusal("invalid_classes", unclear)
    low, high = entry.get("min", -math.inf), entry.get("max", math.inf)
    if not low < high:
        raise Refusal("invalid_classes", f"{unclear}; {entry['label']!r} has no value between its min and max")
    return {"label": entry["label"], "values": None, "min": low, "max": high}


def _is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _apart(table, unclear):
    ranges = [entry for entry in table if entry["values"] is None]
    seen = set()
    for entry in table:
        if entry["values"] is None:
            continue
        if seen.intersection(entry["values"]):
            raise Refusal("invalid_classes", f"{unclear}; {sorted(seen.intersection(entry['values']))} is in more than one class")
        seen.update(entry["values"])
    for i, first in enumerate(ranges):
        for second in ranges[i + 1:]:
            if first["min"] < second["max"] and second["min"] < first["max"]:
                raise Refusal("invalid_classes", f"{unclear}; {first['label']!r} and {second['label']!r} overlap")
        inside = [v for v in seen if first["min"] <= v < first["max"]]
        if inside:
            raise Refusal("invalid_classes", f"{unclear}; {sorted(inside)} is in more than one class")
