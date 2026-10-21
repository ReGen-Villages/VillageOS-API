import pytest

from quadrat import Refusal, bbox_zone, zonal_stats
from conftest import square


def test_bbox_becomes_a_closed_rectangle():
    assert bbox_zone(2, 6, 4, 8) == square(2, 6, 4, 8)


@pytest.mark.parametrize("west, south, east, north", [
    (4, 6, 2, 8),
    (2, 8, 4, 6),
    (2, 6, 2, 8),
    (2, 6, 4, 6),
    (-181, 6, 4, 8),
    (2, 6, 181, 8),
    (2, -91, 4, 8),
    (2, 6, 4, 91),
])
def test_bbox_that_is_inverted_empty_or_off_the_globe_is_refused(west, south, east, north):
    with pytest.raises(Refusal) as refused:
        bbox_zone(west, south, east, north)
    assert refused.value.code == "invalid_bbox"


def test_a_hole_in_a_polygon_is_left_out(degrees_grid):
    outer = [[1, 5], [5, 5], [5, 9], [1, 9], [1, 5]]
    hole = [[2, 6], [4, 6], [4, 8], [2, 8], [2, 6]]
    result = zonal_stats(degrees_grid, {"type": "Polygon", "coordinates": [outer, hole]})
    ring_of_cells = [row * 10 + column for row in range(1, 5) for column in range(1, 5) if not (row in (2, 3) and column in (2, 3))]
    assert result["count"] == 12
    assert result["mean"] == pytest.approx(sum(ring_of_cells) / 12)


def test_a_self_crossing_polygon_is_refused(degrees_grid):
    bowtie = {"type": "Polygon", "coordinates": [[[2, 6], [4, 8], [4, 6], [2, 8], [2, 6]]]}
    with pytest.raises(Refusal) as refused:
        zonal_stats(degrees_grid, bowtie)
    assert refused.value.code == "invalid_geometry"


def test_a_polygon_off_the_globe_is_refused(degrees_grid):
    with pytest.raises(Refusal) as refused:
        zonal_stats(degrees_grid, square(2, 6, 4, 95))
    assert refused.value.code == "invalid_geometry"
