from rasterio.warp import transform_bounds

from .source import open_raster


def describe(path):
    with open_raster(path) as dataset:
        west, south, east, north = dataset.bounds
        wgs84 = transform_bounds(dataset.crs, "EPSG:4326", *dataset.bounds)
        return {
            "crs": dataset.crs.to_string(),
            "width": dataset.width,
            "height": dataset.height,
            "resolution": [float(step) for step in dataset.res],
            "bounds": {"west": west, "south": south, "east": east, "north": north},
            "bounds_wgs84": dict(zip(("west", "south", "east", "north"), wgs84)),
            "bands": [
                {"band": index, "dtype": dtype, "nodata": nodata}
                for index, dtype, nodata in zip(dataset.indexes, dataset.dtypes, dataset.nodatavals)
            ],
        }
