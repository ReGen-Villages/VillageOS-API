import subprocess
import sys
from pathlib import Path

import numpy as np
import pytest
import rasterio
from rasterio.transform import from_origin

NODATA = -9999.0


def write_grid(path, crs, west, north, cell):
    values = np.arange(100, dtype="float32").reshape(10, 10)
    values[9, 9] = NODATA
    with rasterio.open(
        path, "w", driver="GTiff", height=10, width=10, count=1, dtype="float32",
        crs=crs, transform=from_origin(west, north, cell, cell), nodata=NODATA,
    ) as dataset:
        dataset.write(values, 1)
    return str(path)


@pytest.fixture
def degrees_grid(tmp_path):
    """Ten by ten one-degree cells over lon 0..10, lat 0..10; the cell at row r, column c holds r*10+c and the south-east corner is nodata."""
    return write_grid(tmp_path / "degrees.tif", "EPSG:4326", 0, 10, 1)


@pytest.fixture
def metres_grid(tmp_path):
    """The same ten by ten values in web-mercator metres, 100 km cells, x 0..1,000,000 and y 0..1,000,000."""
    return write_grid(tmp_path / "metres.tif", "EPSG:3857", 0, 1_000_000, 100_000)


def square(west, south, east, north):
    return {
        "type": "Polygon",
        "coordinates": [[[west, south], [east, south], [east, north], [west, north], [west, south]]],
    }


def write_png(path, pixels):
    """pixels is rows of (red, green, blue) triples."""
    data = np.array(pixels, dtype="uint8")
    with rasterio.open(path, "w", driver="PNG", height=data.shape[0], width=data.shape[1], count=3, dtype="uint8") as png:
        for band in range(3):
            png.write(data[:, :, band], band + 1)
    return str(path)


class WebHost:
    """A web server in its own process, serving a folder, that can say what was asked of it."""

    def __init__(self, process, port):
        self._process = process
        self.base = f"http://127.0.0.1:{port}"

    def address(self, name):
        return f"{self.base}/{name}"

    def requests(self):
        import json
        import urllib.request
        with urllib.request.urlopen(f"{self.base}/__log") as answer:
            return json.load(answer)

    def bytes_served(self):
        return sum(request["bytes"] for request in self.requests())


@pytest.fixture
def web_host(tmp_path):
    import subprocess
    import sys
    folder = tmp_path / "web"
    folder.mkdir()
    process = subprocess.Popen(
        [sys.executable, str(Path(__file__).parent / "range_server.py"), str(folder)], stdout=subprocess.PIPE, text=True)
    host = WebHost(process, int(process.stdout.readline()))
    host.folder = folder
    yield host
    process.kill()
    process.wait()


@pytest.fixture
def big_cog(web_host):
    """A 2048 by 2048 float32 Cloud Optimized GeoTIFF, about 15 MB, over lon 0..10 and lat 0..10, served by web_host as big.tif."""
    size = 2048
    values = np.random.default_rng(1).random((size, size), dtype="float32")
    with rasterio.open(
        web_host.folder / "big.tif", "w", driver="COG", height=size, width=size, count=1, dtype="float32",
        crs="EPSG:4326", transform=from_origin(0, 10, 10 / size, 10 / size), compress="deflate", blocksize=256,
    ) as dataset:
        dataset.write(values, 1)
    return web_host.address("big.tif")


@pytest.fixture
def land_cover_grid(tmp_path):
    """Ten by ten one-degree cells over lon 0..10, lat 0..10 holding land-cover codes: 10 over the northern half
    (50 cells), 20 over the south-west (25), 30 over the south-middle (20), and nodata in the last column of the
    southern half (5)."""
    values = np.full((10, 10), 10, dtype="uint8")
    values[5:, :5] = 20
    values[5:, 5:9] = 30
    values[5:, 9] = 255
    path = tmp_path / "land-cover.tif"
    with rasterio.open(
        path, "w", driver="GTiff", height=10, width=10, count=1, dtype="uint8",
        crs="EPSG:4326", transform=from_origin(0, 10, 1, 1), nodata=255,
    ) as dataset:
        dataset.write(values, 1)
    return str(path)
