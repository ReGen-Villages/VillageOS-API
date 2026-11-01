import pytest

from quadrat import Refusal, zonal_stats
from conftest import square


def test_polygon_over_whole_cells_reports_plain_statistics(degrees_grid):
    result = zonal_stats(degrees_grid, square(2, 6, 4, 8))
    assert result["count"] == 4
    assert result["min"] == 22 and result["max"] == 33
    assert result["mean"] == pytest.approx(27.5)
    assert result["median"] == pytest.approx(27.5)
    assert result["nodata_cells"] == 0


def test_a_half_covered_cell_counts_half(degrees_grid):
    result = zonal_stats(degrees_grid, square(2, 7, 3.5, 8))
    assert result["count"] == pytest.approx(1.5)
    assert result["mean"] == pytest.approx((22 + 23 * 0.5) / 1.5)


def test_nodata_cells_are_left_out_of_the_statistics_and_counted(degrees_grid):
    result = zonal_stats(degrees_grid, square(8, 0, 10, 1))
    assert result["count"] == 1
    assert result["mean"] == 98
    assert result["nodata_cells"] == pytest.approx(1)


def test_polygon_in_a_different_crs_is_reprojected(metres_grid):
    from pyproj import Transformer
    to_wgs84 = Transformer.from_crs("EPSG:3857", "EPSG:4326", always_xy=True)
    west, south = to_wgs84.transform(200_000, 700_000)
    east, north = to_wgs84.transform(400_000, 900_000)
    result = zonal_stats(metres_grid, square(west, south, east, north))
    assert result["count"] == pytest.approx(4, abs=0.2)
    assert result["mean"] == pytest.approx(17.5, abs=0.5)


def test_polygon_wholly_outside_the_raster_is_refused(degrees_grid):
    with pytest.raises(Refusal) as refused:
        zonal_stats(degrees_grid, square(40, 40, 41, 41))
    assert refused.value.code == "outside_raster"


def test_a_geometry_that_is_not_a_polygon_is_refused(degrees_grid):
    with pytest.raises(Refusal) as refused:
        zonal_stats(degrees_grid, {"type": "Point", "coordinates": [2.5, 7.5]})
    assert refused.value.code == "unsupported_geometry"
