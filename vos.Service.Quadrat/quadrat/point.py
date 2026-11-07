import math

import numpy.ma as masked_arrays
from rasterio.windows import Window

from .refusal import Refusal
from .source import from_wgs84, open_raster

RESAMPLINGS = ("nearest", "bilinear")


def sample_point(path, lon, lat, resampling="nearest", band=1):
    if resampling not in RESAMPLINGS:
        raise Refusal("unknown_resampling", f"{resampling!r} is not one of {', '.join(RESAMPLINGS)}")
    with open_raster(path) as dataset:
        x, y = from_wgs84(dataset).transform(lon, lat)
        west, south, east, north = dataset.bounds
        if not (west <= x < east and south < y <= north):
            raise Refusal("outside_raster", f"({lon}, {lat}) lies outside the raster")
        column, row = ~dataset.transform @ (x, y)
        if resampling == "nearest":
            value = _cell(dataset, band, int(row), int(column))
        else:
            value = _bilinear(dataset, band, row, column)
        return {
            "value": value,
            "nodata": value is None,
            "lon": lon,
            "lat": lat,
            "band": band,
            "resampling": resampling,
            "row": int(row),
            "column": int(column),
        }


def _cell(dataset, band, row, column):
    cell = dataset.read(band, window=Window(column, row, 1, 1), masked=True)[0, 0]
    return None if cell is masked_arrays.masked else float(cell)


def _bilinear(dataset, band, row, column):
    # A cell's value belongs to its centre, half a cell in from the corner the transform measures to.
    row_from_centre = row - 0.5
    column_from_centre = column - 0.5
    top, left = math.floor(row_from_centre), math.floor(column_from_centre)
    down, right = row_from_centre - top, column_from_centre - left
    total = 0.0
    for r, weight_r in ((top, 1 - down), (top + 1, down)):
        for c, weight_c in ((left, 1 - right), (left + 1, right)):
            weight = weight_r * weight_c
            if weight == 0:
                continue
            cell = _cell(dataset, band, min(max(r, 0), dataset.height - 1), min(max(c, 0), dataset.width - 1))
            if cell is None:
                return None
            total += cell * weight
    return total
