import numpy as np
import pytest
import rasterio
from pyproj import Geod
from rasterio.transform import from_origin
from shapely import segmentize
from shapely.geometry import shape

from quadrat import Refusal, classify, histogram
from conftest import square

WHOLE = square(0, 0, 10, 10)


def ground_area(geometry):
    """The area of a box of latitudes and longitudes, whose edges run along parallels and meridians, not great circles."""
    return abs(Geod(ellps="WGS84").geometry_area_perimeter(segmentize(shape(geometry), 0.01))[0])


def by_value(result):
    return {entry["value"]: entry for entry in result["classes"]}


def test_a_histogram_counts_the_cells_of_each_value_and_the_nodata_cells(land_cover_grid):
    result = histogram(land_cover_grid, WHOLE)
    cells = {value: entry["cells"] for value, entry in by_value(result).items()}
    assert cells == {10: pytest.approx(50), 20: pytest.approx(25), 30: pytest.approx(20)}
    assert result["nodata"]["cells"] == pytest.approx(5)
    assert result["covered_cells"] == pytest.approx(100)


def test_each_class_has_the_share_of_the_zone_it_covers_and_its_ground_area(land_cover_grid):
    result = histogram(land_cover_grid, WHOLE)
    entry = by_value(result)[20]
    assert entry["fraction"] == pytest.approx(0.25)
    assert entry["area_m2"] == pytest.approx(0.25 * ground_area(WHOLE), rel=1e-6)


def test_the_areas_and_the_nodata_area_make_up_the_zone(land_cover_grid):
    result = histogram(land_cover_grid, WHOLE)
    total = sum(entry["area_m2"] for entry in result["classes"]) + result["nodata"]["area_m2"]
    assert total == pytest.approx(result["area_m2"], rel=1e-9)
    assert result["area_m2"] == pytest.approx(ground_area(WHOLE), rel=1e-6)


def test_a_cell_the_zone_only_half_covers_counts_half(land_cover_grid):
    result = histogram(land_cover_grid, square(0, 4.5, 2, 5.5))
    assert {value: entry["cells"] for value, entry in by_value(result).items()} == {10: pytest.approx(1.0), 20: pytest.approx(1.0)}


def test_a_zone_outside_the_raster_is_refused(land_cover_grid):
    with pytest.raises(Refusal) as refused:
        histogram(land_cover_grid, square(50, 50, 60, 60))
    assert refused.value.code == "outside_raster"


def test_a_zone_over_the_cell_limit_is_refused_before_it_is_read(land_cover_grid):
    with pytest.raises(Refusal) as refused:
        histogram(land_cover_grid, WHOLE, max_cells=50)
    assert refused.value.code == "too_many_cells"


def test_a_raster_with_too_many_distinct_values_is_not_a_land_cover_and_is_refused(tmp_path):
    path = tmp_path / "continuous.tif"
    with rasterio.open(path, "w", driver="GTiff", height=100, width=100, count=1, dtype="float32", crs="EPSG:4326", transform=from_origin(0, 10, 0.1, 0.1)) as dataset:
        dataset.write(np.random.default_rng(3).random((100, 100), dtype="float32"), 1)
    with pytest.raises(Refusal) as refused:
        histogram(str(path), WHOLE)
    assert refused.value.code == "too_many_values"


TREES_AND_OPEN = [{"label": "trees", "values": [10]}, {"label": "open", "values": [20, 30]}]


def test_classify_adds_up_the_values_of_each_class(land_cover_grid):
    result = classify(land_cover_grid, WHOLE, TREES_AND_OPEN)
    cells = {entry["label"]: entry["cells"] for entry in result["classes"]}
    assert cells == {"trees": pytest.approx(50), "open": pytest.approx(45)}
    assert result["unclassified"]["cells"] == 0
    assert result["nodata"]["cells"] == pytest.approx(5)


def test_classes_come_back_in_the_order_given_and_a_class_with_no_cells_is_still_listed(land_cover_grid):
    result = classify(land_cover_grid, WHOLE, [{"label": "water", "values": [99]}, *TREES_AND_OPEN])
    assert [(entry["label"], entry["cells"]) for entry in result["classes"]] == [("water", 0), ("trees", pytest.approx(50)), ("open", pytest.approx(45))]


def test_a_value_no_class_names_is_reported_as_unclassified_never_guessed(land_cover_grid):
    result = classify(land_cover_grid, WHOLE, [{"label": "trees", "values": [10]}])
    assert result["unclassified"]["values"] == [20, 30]
    assert result["unclassified"]["cells"] == pytest.approx(45)
    assert result["unclassified"]["area_m2"] == pytest.approx(0.45 * ground_area(WHOLE), rel=1e-6)


@pytest.mark.parametrize("table", [
    None, [], "trees", {"label": "trees", "values": [10]},
    [{"values": [10]}], [{"label": "", "values": [10]}], [{"label": "trees"}],
    [{"label": "trees", "values": []}], [{"label": "trees", "values": ["ten"]}], [{"label": "trees", "values": [True]}],
    [{"label": "trees", "values": [10]}, {"label": "trees", "values": [20]}],
    [{"label": "a", "values": [10]}, {"label": "b", "values": [10]}],
    [{"label": "trees", "values": [10], "min": 0}],
    ["trees"],
])
def test_a_class_table_that_is_not_a_clear_one_is_refused(land_cover_grid, table):
    with pytest.raises(Refusal) as refused:
        classify(land_cover_grid, WHOLE, table)
    assert refused.value.code == "invalid_classes"
