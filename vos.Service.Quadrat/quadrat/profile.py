import math

import numpy
from pyproj import Geod
from rasterio.windows import Window

from .point import RESAMPLINGS
from .refusal import Refusal
from .source import from_wgs84, open_raster

GEOD = Geod(ellps="WGS84")
# Samples closer than this to the line's end are the end itself, not another sample.
SAME_PLACE_M = 1e-6


def profile(path, line, resampling="nearest", spacing=None, samples=None, max_cells=None, max_samples=None):
    """The raster's values at points along a line: a sample every `spacing` metres, or `samples` of them spread evenly.

    The line is a GeoJSON LineString in WGS84 degrees, followed vertex to vertex along the ellipsoid, and distances are
    metres along it. The first sample is the line's start and the last its end. Each sample reads as `point` does at
    the same place; a sample off the raster or on a nodata cell has no value and says why.
    """
    if resampling not in RESAMPLINGS:
        raise Refusal("unknown_resampling", f"{resampling!r} is not one of {', '.join(RESAMPLINGS)}")
    vertices = _vertices(line)
    legs = [GEOD.inv(a[0], a[1], b[0], b[1]) for a, b in zip(vertices, vertices[1:])]
    starts = numpy.concatenate([[0.0], numpy.cumsum([leg[2] for leg in legs])])
    length = float(starts[-1])
    if length <= 0:
        raise Refusal("invalid_geometry", "a line has two different points at least")
    distances = _distances(length, spacing, samples, max_samples)
    places = _places(vertices, legs, starts, distances)
    with open_raster(path) as dataset:
        found = _read(dataset, places, resampling, max_cells)
    outside = sum(1 for _, reason in found if reason == "outside_raster")
    if outside == len(found):
        raise Refusal("outside_raster", "the line lies outside the raster")
    return {
        "length_m": length,
        "resampling": resampling,
        "samples": [
            {"distance_m": float(distance), "lon": float(lon), "lat": float(lat), "value": value, "reason": reason}
            for distance, (lon, lat), (value, reason) in zip(distances, places, found)
        ],
        "outside_samples": outside,
        "nodata_samples": sum(1 for _, reason in found if reason == "nodata"),
    }


def _vertices(line):
    if not isinstance(line, dict) or line.get("type") != "LineString":
        kind = line.get("type") if isinstance(line, dict) else type(line).__name__
        raise Refusal("unsupported_geometry", f"a line is a GeoJSON LineString, not {kind!r}")
    unclear = "a line is [[lon, lat], ...] of two points at least, within -180..180 and -90..90, and not all the same point"
    coordinates = line.get("coordinates")
    if not isinstance(coordinates, list) or len(coordinates) < 2:
        raise Refusal("invalid_geometry", unclear)
    vertices = []
    for point in coordinates:
        if not isinstance(point, (list, tuple)) or len(point) < 2 or not all(_is_number(v) for v in point[:2]):
            raise Refusal("invalid_geometry", unclear)
        lon, lat = float(point[0]), float(point[1])
        if not (-180 <= lon <= 180 and -90 <= lat <= 90):
            raise Refusal("invalid_geometry", unclear)
        vertices.append((lon, lat))
    if len(set(vertices)) < 2:
        raise Refusal("invalid_geometry", unclear)
    return vertices


def _is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _distances(length, spacing, samples, max_samples):
    if spacing is None and samples is None:
        raise Refusal("missing_input", "give 'samples' (how many points along the line) or 'spacing' (metres between points)")
    if spacing is not None and samples is not None:
        raise Refusal("ambiguous_sampling", "give 'samples' or 'spacing', not both")
    if samples is not None:
        count = _count(samples)
        _within(count, max_samples)
        return numpy.linspace(0.0, length, count)
    if not _is_number(spacing) or spacing <= 0:
        raise Refusal("invalid_sampling", f"'spacing' is a number of metres above 0, not {spacing!r}")
    _within(math.floor(length / spacing) + 2, max_samples)
    steps = numpy.arange(0.0, length - SAME_PLACE_M, spacing)
    return numpy.append(steps, length)


def _count(samples):
    if isinstance(samples, float) and samples.is_integer():
        samples = int(samples)
    if not isinstance(samples, int) or isinstance(samples, bool) or samples < 2:
        raise Refusal("invalid_sampling", f"'samples' is a whole number of 2 or more (the two ends), not {samples!r}")
    return samples


def _within(count, max_samples):
    if max_samples is not None and count > max_samples:
        raise Refusal(
            "too_many_samples",
            f"this profile needs {count} samples, and this service takes at most {max_samples} in one request: "
            f"use a larger spacing or fewer samples")


def _places(vertices, legs, starts, distances):
    """The WGS84 position of each distance along the line, found on the leg that holds it."""
    places = []
    for distance in distances:
        index = min(int(numpy.searchsorted(starts, distance, side="right")) - 1, len(legs) - 1)
        lon, lat = vertices[index]
        if distance - starts[index] > 0:
            lon, lat, _ = GEOD.fwd(lon, lat, legs[index][0], float(distance - starts[index]))
        places.append((lon, lat))
    return places


def _read(dataset, places, resampling, max_cells):
    """(value, reason) for each place: one window holding every cell the samples touch is read, once."""
    to_raster = from_wgs84(dataset)
    xs, ys = to_raster.transform([p[0] for p in places], [p[1] for p in places])
    inverse = ~dataset.transform
    columns = inverse.a * numpy.asarray(xs) + inverse.b * numpy.asarray(ys) + inverse.c
    rows = inverse.d * numpy.asarray(xs) + inverse.e * numpy.asarray(ys) + inverse.f
    within = numpy.isfinite(columns) & numpy.isfinite(rows) & (columns >= 0) & (columns < dataset.width) & (rows >= 0) & (rows < dataset.height)
    if not within.any():
        return [(None, "outside_raster")] * len(places)
    first_column, last_column = int(math.floor(columns[within].min())) - 1, int(math.floor(columns[within].max())) + 2
    first_row, last_row = int(math.floor(rows[within].min())) - 1, int(math.floor(rows[within].max())) + 2
    first_column, first_row = max(first_column, 0), max(first_row, 0)
    last_column, last_row = min(last_column, dataset.width), min(last_row, dataset.height)
    window_cells = (last_column - first_column) * (last_row - first_row)
    if max_cells is not None and window_cells > max_cells:
        raise Refusal(
            "too_many_cells",
            f"this line needs a window of {window_cells} cells of the raster to be read, and this service reads at most "
            f"{max_cells} in one request: use a shorter line or a coarser raster")
    cells = dataset.read(1, window=Window(first_column, first_row, last_column - first_column, last_row - first_row), masked=True)
    found = []
    for inside, column, row in zip(within, columns, rows):
        if not inside:
            found.append((None, "outside_raster"))
            continue
        if resampling == "nearest":
            value = _cell(cells, int(row) - first_row, int(column) - first_column)
        else:
            value = _bilinear(cells, dataset, row, column, first_row, first_column)
        found.append((value, None) if value is not None else (None, "nodata"))
    return found


def _cell(cells, row, column):
    cell = cells[row, column]
    return None if cell is numpy.ma.masked else float(cell)


def _bilinear(cells, dataset, row, column, first_row, first_column):
    # A cell's value belongs to its centre, half a cell in from the corner the transform measures to, as `point` reads it.
    row_from_centre, column_from_centre = row - 0.5, column - 0.5
    top, left = math.floor(row_from_centre), math.floor(column_from_centre)
    down, right = row_from_centre - top, column_from_centre - left
    total = 0.0
    for r, weight_r in ((top, 1 - down), (top + 1, down)):
        for c, weight_c in ((left, 1 - right), (left + 1, right)):
            weight = weight_r * weight_c
            if weight == 0:
                continue
            cell = _cell(cells, min(max(r, 0), dataset.height - 1) - first_row, min(max(c, 0), dataset.width - 1) - first_column)
            if cell is None:
                return None
            total += cell * weight
    return total
