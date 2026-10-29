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
