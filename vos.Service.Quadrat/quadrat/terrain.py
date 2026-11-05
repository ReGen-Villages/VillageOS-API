import os
import tempfile
import warnings
from contextlib import contextmanager

import numpy
import rasterio
from pyproj import Transformer
from rasterio.errors import NotGeoreferencedWarning, RasterioIOError
from rasterio.transform import from_bounds

from .refusal import Refusal

WORLD = 20037508.342789244
ENCODINGS = {
    "terrarium": lambda red, green, blue: red * 256.0 + green + blue / 256.0 - 32768.0,
    "mapbox": lambda red, green, blue: -10000.0 + (red * 65536.0 + green * 256.0 + blue) * 0.1,
}
TO_WGS84 = Transformer.from_crs("EPSG:3857", "EPSG:4326", always_xy=True)


def tile_bounds(z, x, y):
    """A web-mercator tile's west, south, east and north in metres; y counts down from the north."""
    if z < 0 or not (0 <= x < 2**z and 0 <= y < 2**z):
        raise Refusal("invalid_tile", f"tile {z}/{x}/{y} does not exist: at zoom {z}, x and y run 0 to {2**z - 1 if z >= 0 else 'n/a'}")
    size = 2 * WORLD / 2**z
    return (-WORLD + x * size, WORLD - (y + 1) * size, -WORLD + (x + 1) * size, WORLD - y * size)


def tile_bounds_wgs84(z, x, y):
    west, south, east, north = tile_bounds(z, x, y)
    (west, east), (south, north) = TO_WGS84.transform([west, east], [south, north])
    return west, south, east, north


def decode_elevation(red, green, blue, encoding):
    if not encoding:
        raise Refusal("unstated_encoding", f"a terrain tile does not say how it holds elevation: state one of {', '.join(ENCODINGS)}")
    if encoding not in ENCODINGS:
        raise Refusal("unknown_encoding", f"{encoding!r} is not an encoding Quadrat decodes: it decodes {', '.join(ENCODINGS)}")
    return ENCODINGS[encoding](*(numpy.asarray(band, dtype="float64") for band in (red, green, blue)))


@contextmanager
def decoded_tile(path, z, x, y, encoding, max_cells=None):
    """Yields the path to a temporary GeoTIFF of the tile's elevations in metres, placed where the tile is.

    The image is a plain PNG with no position of its own, so which tile it is, and how it holds elevation,
    must be stated: neither is guessed.
    """
    bounds = tile_bounds(z, x, y)
    decode_elevation(0, 0, 0, encoding)
    try:
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", NotGeoreferencedWarning)
            with rasterio.open(path) as image:
                if image.count < 3:
                    raise Refusal("not_a_terrain_tile", f"{path} has {image.count} band(s); a terrain tile holds elevation in the red, green and blue bands")
                height, width = image.height, image.width
                if max_cells is not None and height * width > max_cells:
                    raise Refusal(
                        "too_many_cells",
                        f"this image is {height * width} cells, and this service decodes at most {max_cells} in one request")
                red, green, blue = image.read([1, 2, 3])
    except RasterioIOError as unreadable:
        raise Refusal("unreadable_raster", f"{path} could not be opened as an image: {unreadable}") from unreadable
    handle, decoded = tempfile.mkstemp(suffix=".tif", prefix="quadrat-tile-")
    os.close(handle)
    try:
        with rasterio.open(
            decoded, "w", driver="GTiff", height=height, width=width, count=1, dtype="float32",
            crs="EPSG:3857", transform=from_bounds(*bounds, width, height),
        ) as tile:
            tile.write(decode_elevation(red, green, blue, encoding).astype("float32"), 1)
        yield decoded
    finally:
        os.remove(decoded)
