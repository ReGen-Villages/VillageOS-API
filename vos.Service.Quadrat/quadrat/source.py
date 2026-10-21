from contextlib import contextmanager

import rasterio
from pyproj import CRS, Transformer
from rasterio.errors import RasterioIOError

from .refusal import Refusal

WGS84 = CRS.from_epsg(4326)


@contextmanager
def open_raster(path):
    try:
        dataset = rasterio.open(path)
    except RasterioIOError as unreadable:
        raise Refusal("unreadable_raster", f"{path} could not be opened as a raster: {unreadable}") from unreadable
    with dataset:
        if dataset.crs is None:
            raise Refusal("no_crs", f"{path} states no coordinate reference system, and one is never guessed")
        yield dataset


def from_wgs84(dataset):
    return Transformer.from_crs(WGS84, CRS.from_wkt(dataset.crs.to_wkt()), always_xy=True)
