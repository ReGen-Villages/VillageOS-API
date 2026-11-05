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


def test_a_zone_over_more_cells_than_the_limit_is_refused_saying_how_many_and_how_few(degrees_grid):
    with pytest.raises(Refusal) as refusal:
        zonal_stats(degrees_grid, square(0, 0, 10, 10), max_cells=99)
    assert refusal.value.code == "too_many_cells"
    assert "100" in refusal.value.reason and "99" in refusal.value.reason


def test_a_zone_over_exactly_the_limit_is_read(degrees_grid):
    assert zonal_stats(degrees_grid, square(0, 8, 2, 10), max_cells=4)["count"] == 4


def test_a_zone_over_one_cell_more_than_the_limit_is_refused(degrees_grid):
    with pytest.raises(Refusal) as refusal:
        zonal_stats(degrees_grid, square(0, 8, 2, 10), max_cells=3)
    assert refusal.value.code == "too_many_cells"


def test_the_cells_counted_are_the_whole_cells_the_zone_touches_not_its_area(degrees_grid):
    # Half a cell in on every side: the zone's area is one cell, the window the read must load is four.
    zone = square(0.5, 8.5, 1.5, 9.5)
    with pytest.raises(Refusal):
        zonal_stats(degrees_grid, zone, max_cells=3)
    assert zonal_stats(degrees_grid, zone, max_cells=4)["covered_cells"] == pytest.approx(1.0)


def test_only_the_part_of_a_zone_over_the_raster_is_counted(degrees_grid):
    assert zonal_stats(degrees_grid, square(-50, -50, 50, 50), max_cells=100)["count"] == 99


def test_a_zone_is_refused_before_the_raster_is_read(degrees_grid, monkeypatch):
    def read(*args, **kwargs):
        raise AssertionError("the raster was read for a zone over the limit")

    monkeypatch.setattr("quadrat.zonal.exact_extract", read)
    with pytest.raises(Refusal) as refusal:
        zonal_stats(degrees_grid, square(0, 0, 10, 10), max_cells=10)
    assert refusal.value.code == "too_many_cells"


def test_with_no_limit_given_any_zone_is_read(degrees_grid):
    assert zonal_stats(degrees_grid, square(0, 0, 10, 10))["covered_cells"] == pytest.approx(100.0)
