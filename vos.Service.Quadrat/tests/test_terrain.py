import os

import numpy as np
import pytest
import rasterio

from conftest import write_png
from quadrat import Refusal, describe, sample_point, zonal_stats
from quadrat.terrain import decode_elevation, decoded_tile, tile_bounds, tile_bounds_wgs84
from quadrat.zone import bbox_zone

WORLD = 20037508.342789244
TATRA_PNG = os.path.join(os.path.dirname(__file__), "..", "samples", "tatra-z10-568-350.terrarium.png")
TATRA_TIF = os.path.join(os.path.dirname(__file__), "..", "samples", "tatra-z10-568-350.tif")


def test_the_world_is_one_tile_at_zoom_zero():
    assert tile_bounds(0, 0, 0) == pytest.approx((-WORLD, -WORLD, WORLD, WORLD))


def test_a_tile_is_a_quarter_of_its_parent():
    assert tile_bounds(1, 1, 0) == pytest.approx((0, 0, WORLD, WORLD))
    assert tile_bounds(1, 0, 1) == pytest.approx((-WORLD, -WORLD, 0, 0))


def test_a_tiles_geographic_bounds():
    west, south, east, north = tile_bounds_wgs84(10, 568, 350)
    assert (west, east) == pytest.approx((19.6875, 20.0390625))
    assert (south, north) == pytest.approx((49.15296965617039, 49.38237278700955))


@pytest.mark.parametrize("z, x, y", [(-1, 0, 0), (1, 2, 0), (1, 0, 2), (1, -1, 0), (0, 0, 1)])
def test_a_tile_outside_its_zoom_is_refused(z, x, y):
    with pytest.raises(Refusal) as refused:
        tile_bounds(z, x, y)
    assert refused.value.code == "invalid_tile"


def test_terrarium_reads_red_green_and_blue_as_a_fraction_of_a_metre_above_32768_below_sea_level():
    elevation = decode_elevation(np.array([135, 128, 0]), np.array([170, 0, 0]), np.array([128, 0, 0]), "terrarium")
    assert elevation.tolist() == [1962.5, 0.0, -32768.0]


def test_mapbox_reads_the_three_bytes_as_tenths_of_a_metre_above_minus_10000():
    elevation = decode_elevation(np.array([1, 0]), np.array([211, 0]), np.array([73, 0]), "mapbox")
    assert elevation.tolist() == pytest.approx([1962.5, -10000.0])


@pytest.mark.parametrize("encoding", [None, ""])
def test_an_encoding_that_is_not_stated_is_refused(encoding):
    with pytest.raises(Refusal) as refused:
        decode_elevation(np.array([0]), np.array([0]), np.array([0]), encoding)
    assert refused.value.code == "unstated_encoding"


def test_an_encoding_that_is_not_known_is_refused_naming_the_known_ones():
    with pytest.raises(Refusal) as refused:
        decode_elevation(np.array([0]), np.array([0]), np.array([0]), "gebco")
    assert refused.value.code == "unknown_encoding"
    assert "terrarium" in refused.value.reason and "mapbox" in refused.value.reason


def test_a_decoded_tile_is_a_raster_placed_where_the_tile_is(tmp_path):
    png = write_png(tmp_path / "tile.png", [[(135, 170, 128), (128, 0, 0)], [(128, 0, 0), (128, 0, 0)]])
    with decoded_tile(png, 1, 1, 0, "terrarium") as raster:
        shape = describe(raster)
        assert shape["crs"] == "EPSG:3857"
        assert (shape["width"], shape["height"]) == (2, 2)
        assert [shape["bounds"][side] for side in ("west", "south", "east", "north")] == pytest.approx([0, 0, WORLD, WORLD])
        north_west = sample_point(raster, lon=10, lat=80)
        assert north_west["value"] == 1962.5


def test_a_decoded_tile_answers_zonal_reads(tmp_path):
    png = write_png(tmp_path / "tile.png", [[(128, 10, 0), (128, 20, 0)], [(128, 30, 0), (128, 40, 0)]])
    with decoded_tile(png, 1, 1, 0, "terrarium") as raster:
        assert zonal_stats(raster, bbox_zone(0, 0, 180, 85.05112877980659))["mean"] == pytest.approx(25.0, abs=0.01)


def test_a_decoded_tile_leaves_nothing_behind(tmp_path):
    png = write_png(tmp_path / "tile.png", [[(128, 0, 0)]])
    with decoded_tile(png, 0, 0, 0, "terrarium") as raster:
        assert os.path.exists(raster)
    assert not os.path.exists(raster)


def test_a_decoded_tile_leaves_nothing_behind_when_the_read_is_refused(tmp_path):
    png = write_png(tmp_path / "tile.png", [[(128, 0, 0)]])
    with pytest.raises(Refusal):
        with decoded_tile(png, 0, 0, 0, "terrarium") as raster:
            kept = raster
            sample_point(raster, lon=0, lat=89)
    assert not os.path.exists(kept)


def test_an_image_that_is_not_three_colour_bands_is_refused(tmp_path):
    path = tmp_path / "grey.png"
    with rasterio.open(path, "w", driver="PNG", height=2, width=2, count=1, dtype="uint8") as png:
        png.write(np.zeros((2, 2), dtype="uint8"), 1)
    with pytest.raises(Refusal) as refused:
        with decoded_tile(str(path), 0, 0, 0, "terrarium"):
            pass
    assert refused.value.code == "not_a_terrain_tile"


def test_a_file_that_is_not_an_image_is_refused_as_unreadable(tmp_path):
    missing = str(tmp_path / "missing.png")
    with pytest.raises(Refusal) as refused:
        with decoded_tile(missing, 0, 0, 0, "terrarium"):
            pass
    assert refused.value.code == "unreadable_raster"


def test_the_encoding_is_checked_before_the_image_is_read(tmp_path):
    with pytest.raises(Refusal) as refused:
        with decoded_tile(str(tmp_path / "missing.png"), 0, 0, 0, None):
            pass
    assert refused.value.code == "unstated_encoding"


@pytest.mark.skipif(not (os.path.exists(TATRA_PNG) and os.path.exists(TATRA_TIF)), reason="the Tatra sample tiles are not in git; see the README")
def test_a_real_terrarium_tile_agrees_with_the_geotiff_of_the_same_tile():
    zone = bbox_zone(19.80, 49.20, 19.95, 49.30)
    expected = zonal_stats(TATRA_TIF, zone)
    with decoded_tile(TATRA_PNG, 10, 568, 350, "terrarium") as raster:
        decoded = zonal_stats(raster, zone)
    assert decoded["mean"] == pytest.approx(expected["mean"], abs=2)
    assert decoded["max"] == pytest.approx(expected["max"], abs=40)
    assert decoded["min"] == pytest.approx(expected["min"], abs=5)


def test_a_tile_image_over_the_limit_is_refused_saying_how_many_cells_before_it_is_read(tmp_path, monkeypatch):
    png = write_png(tmp_path / "tile.png", [[(128, 0, 0)] * 4] * 4)

    def read(*args, **kwargs):
        raise AssertionError("the image was read for a tile over the limit")

    monkeypatch.setattr("rasterio.io.DatasetReader.read", read)
    with pytest.raises(Refusal) as refusal:
        with decoded_tile(png, 0, 0, 0, "terrarium", max_cells=15):
            pass
    assert refusal.value.code == "too_many_cells"
    assert "16" in refusal.value.reason and "15" in refusal.value.reason


def test_a_tile_image_at_exactly_the_limit_is_decoded(tmp_path):
    png = write_png(tmp_path / "tile.png", [[(128, 0, 0)] * 4] * 4)
    with decoded_tile(png, 0, 0, 0, "terrarium", max_cells=16) as raster:
        assert describe(raster)["width"] == 4
