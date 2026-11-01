from quadrat import describe


def test_describe_reports_grid_shape_and_crs(degrees_grid):
    result = describe(degrees_grid)
    assert result["width"] == 10
    assert result["height"] == 10
    assert result["crs"] == "EPSG:4326"
    assert result["resolution"] == [1.0, 1.0]


def test_describe_reports_bands_and_nodata(degrees_grid):
    result = describe(degrees_grid)
    assert result["bands"] == [{"band": 1, "dtype": "float32", "nodata": -9999.0}]


def test_describe_reports_bounds_in_the_rasters_own_crs_and_in_wgs84(metres_grid):
    result = describe(metres_grid)
    assert result["bounds"] == {"west": 0.0, "south": 0.0, "east": 1_000_000.0, "north": 1_000_000.0}
    assert result["crs"] == "EPSG:3857"
    wgs84 = result["bounds_wgs84"]
    assert wgs84["west"] == 0.0 and wgs84["south"] == 0.0
    assert 8.9 < wgs84["east"] < 9.0 and 8.9 < wgs84["north"] < 9.0
