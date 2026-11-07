import pytest
from pyproj import Transformer

from quadrat import Refusal, sample_point


def test_nearest_reads_the_cell_the_point_falls_in(degrees_grid):
    assert sample_point(degrees_grid, lon=2.5, lat=7.5)["value"] == 22.0


def test_nearest_at_the_north_west_corner_reads_the_first_cell(degrees_grid):
    assert sample_point(degrees_grid, lon=0.1, lat=9.9)["value"] == 0.0


def test_bilinear_interpolates_between_cell_centres(degrees_grid):
    result = sample_point(degrees_grid, lon=3.0, lat=7.5, resampling="bilinear")
    assert result["value"] == pytest.approx(22.5)


def test_point_on_a_nodata_cell_has_no_value(degrees_grid):
    result = sample_point(degrees_grid, lon=9.5, lat=0.5)
    assert result["value"] is None
    assert result["nodata"] is True


def test_wgs84_point_is_reprojected_into_the_rasters_crs(metres_grid):
    lon, lat = Transformer.from_crs("EPSG:3857", "EPSG:4326", always_xy=True).transform(250_000, 750_000)
    assert sample_point(metres_grid, lon=lon, lat=lat)["value"] == 22.0


def test_point_outside_the_raster_is_refused_with_a_reason(degrees_grid):
    with pytest.raises(Refusal) as refused:
        sample_point(degrees_grid, lon=50, lat=50)
    assert refused.value.code == "outside_raster"


def test_unknown_resampling_is_refused(degrees_grid):
    with pytest.raises(Refusal) as refused:
        sample_point(degrees_grid, lon=2.5, lat=7.5, resampling="cubic-wizardry")
    assert refused.value.code == "unknown_resampling"


def test_missing_file_is_refused(tmp_path):
    with pytest.raises(Refusal) as refused:
        sample_point(str(tmp_path / "nope.tif"), lon=0, lat=0)
    assert refused.value.code == "unreadable_raster"
