import pytest
from pyproj import Geod, Transformer

from quadrat import Refusal, profile, sample_point

GEOD = Geod(ellps="WGS84")


def line(*points):
    return {"type": "LineString", "coordinates": [list(point) for point in points]}


def length_of(geometry):
    coordinates = geometry["coordinates"]
    return sum(GEOD.inv(a[0], a[1], b[0], b[1])[2] for a, b in zip(coordinates, coordinates[1:]))


# degrees_grid holds r*10+c over lon 0..10, lat 0..10: row 2 is lat 7..8, so along lat 7.5 the cell values are 20, 21, 22, ...
ALONG_A_ROW = line((0.5, 7.5), (4.5, 7.5))


def values(result):
    return [sample["value"] for sample in result["samples"]]


def test_samples_spread_evenly_between_the_ends_read_the_cells_under_them(degrees_grid):
    result = profile(degrees_grid, ALONG_A_ROW, samples=5)
    assert values(result) == [20, 21, 22, 23, 24]
    assert result["samples"][0]["distance_m"] == 0


def test_the_profile_states_the_length_of_the_line_on_the_ground(degrees_grid):
    result = profile(degrees_grid, ALONG_A_ROW, samples=5)
    assert result["length_m"] == pytest.approx(length_of(ALONG_A_ROW), rel=1e-9)
    assert result["samples"][-1]["distance_m"] == pytest.approx(result["length_m"], rel=1e-9)


def test_the_first_and_last_samples_sit_on_the_ends_of_the_line(degrees_grid):
    samples = profile(degrees_grid, ALONG_A_ROW, samples=5)["samples"]
    first, last = samples[0], samples[-1]
    assert (first["lon"], first["lat"]) == pytest.approx((0.5, 7.5))
    assert (last["lon"], last["lat"]) == pytest.approx((4.5, 7.5))


def test_distances_are_geodesic_metres_along_the_line(degrees_grid):
    for sample in profile(degrees_grid, ALONG_A_ROW, samples=5)["samples"]:
        assert GEOD.inv(0.5, 7.5, sample["lon"], sample["lat"])[2] == pytest.approx(sample["distance_m"], rel=1e-6, abs=1e-6)


def test_spacing_takes_a_sample_every_so_many_metres_and_ends_on_the_last_point(degrees_grid):
    length = length_of(ALONG_A_ROW)
    result = profile(degrees_grid, ALONG_A_ROW, spacing=length / 2.5)
    assert [sample["distance_m"] for sample in result["samples"]] == pytest.approx([0, 0.4 * length, 0.8 * length, length], rel=1e-9)


def test_a_spacing_that_divides_the_line_does_not_repeat_the_last_point(degrees_grid):
    result = profile(degrees_grid, ALONG_A_ROW, spacing=length_of(ALONG_A_ROW) / 4)
    assert len(result["samples"]) == 5


def test_a_line_with_corners_is_followed_vertex_to_vertex(degrees_grid):
    bent = line((0.5, 7.5), (2.5, 7.5), (2.5, 4.5))
    first_leg = length_of(line((0.5, 7.5), (2.5, 7.5)))
    result = profile(degrees_grid, bent, spacing=first_leg / 2)
    assert result["length_m"] == pytest.approx(length_of(bent), rel=1e-9)
    corner = [s for s in result["samples"] if s["distance_m"] == pytest.approx(first_leg, rel=1e-6)]
    assert len(corner) == 1 and (corner[0]["lon"], corner[0]["lat"]) == pytest.approx((2.5, 7.5), abs=1e-6)
    after = [s for s in result["samples"] if s["distance_m"] > first_leg * 1.01]
    assert after and all(s["lon"] == pytest.approx(2.5, abs=1e-6) for s in after)


@pytest.mark.parametrize("resampling", ["nearest", "bilinear"])
def test_each_sample_is_what_a_point_read_gives_at_the_same_place(degrees_grid, resampling):
    diagonal = line((0.3, 9.4), (8.6, 0.7))
    samples = profile(degrees_grid, diagonal, samples=40, resampling=resampling)["samples"]
    for sample in samples:
        point = sample_point(degrees_grid, sample["lon"], sample["lat"], resampling)
        if point["value"] is None:
            assert sample["value"] is None
        else:
            assert sample["value"] == pytest.approx(point["value"])


@pytest.mark.parametrize("resampling", ["nearest", "bilinear"])
def test_it_agrees_with_a_point_read_on_a_raster_in_metres(metres_grid, resampling):
    to_degrees = Transformer.from_crs("EPSG:3857", "EPSG:4326", always_xy=True)
    start, end = to_degrees.transform(150_000, 850_000), to_degrees.transform(650_000, 250_000)
    for sample in profile(metres_grid, line(start, end), samples=25, resampling=resampling)["samples"]:
        assert sample["value"] == pytest.approx(sample_point(metres_grid, sample["lon"], sample["lat"], resampling)["value"])


def test_bilinear_interpolates_between_cell_centres(degrees_grid):
    result = profile(degrees_grid, line((1.5, 7.5), (2.5, 7.5)), samples=3, resampling="bilinear")
    assert values(result) == pytest.approx([21, 21.5, 22], abs=0.1)


def test_a_sample_on_a_nodata_cell_has_no_value_and_says_why(degrees_grid):
    result = profile(degrees_grid, line((8.5, 0.5), (9.5, 0.5)), samples=3)
    assert values(result) == [98, None, None]
    assert [sample["reason"] for sample in result["samples"]] == [None, "nodata", "nodata"]
    assert result["nodata_samples"] == 2 and result["outside_samples"] == 0


def test_the_samples_beyond_the_raster_have_no_value_and_are_counted(degrees_grid):
    result = profile(degrees_grid, line((8.5, 5.5), (12.5, 5.5)), samples=5)
    assert values(result) == [48, 49, None, None, None]
    assert [sample["reason"] for sample in result["samples"]][2:] == ["outside_raster"] * 3
    assert result["outside_samples"] == 3


def test_a_line_wholly_outside_the_raster_is_refused(degrees_grid):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, line((50, 50), (60, 50)), samples=5)
    assert refused.value.code == "outside_raster"


def test_the_resampling_is_stated_in_the_answer(degrees_grid):
    assert profile(degrees_grid, ALONG_A_ROW, samples=3, resampling="bilinear")["resampling"] == "bilinear"
    assert profile(degrees_grid, ALONG_A_ROW, samples=3)["resampling"] == "nearest"


def test_an_unknown_resampling_is_refused(degrees_grid):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, ALONG_A_ROW, samples=3, resampling="cubic")
    assert refused.value.code == "unknown_resampling"


@pytest.mark.parametrize("geometry", [
    {"type": "Point", "coordinates": [1, 1]},
    {"type": "Polygon", "coordinates": [[[0, 0], [1, 0], [1, 1], [0, 0]]]},
    {"type": "MultiLineString", "coordinates": [[[0, 0], [1, 1]]]},
    {},
])
def test_only_a_line_string_is_a_line(degrees_grid, geometry):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, geometry, samples=3)
    assert refused.value.code == "unsupported_geometry"


@pytest.mark.parametrize("coordinates", [
    [[1, 1]], [], [[1, 1], [1, 1]], [[0, 0], [200, 0]], [[0, 0], [0, 95]],
    [[0, 0], ["a", 1]], [[0, 0], [1]], [[0, 0], [True, 1]], [[0, 0], [float("nan"), 1]], "line",
])
def test_a_line_that_is_not_a_clear_one_is_refused(degrees_grid, coordinates):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, {"type": "LineString", "coordinates": coordinates}, samples=3)
    assert refused.value.code == "invalid_geometry"


def test_one_way_of_sampling_must_be_given(degrees_grid):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, ALONG_A_ROW)
    assert refused.value.code == "missing_input"


def test_samples_and_spacing_together_are_ambiguous(degrees_grid):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, ALONG_A_ROW, samples=5, spacing=1000)
    assert refused.value.code == "ambiguous_sampling"


@pytest.mark.parametrize("sampling", [
    {"samples": 1}, {"samples": 0}, {"samples": -3}, {"samples": 2.5}, {"samples": "5"}, {"samples": True},
    {"spacing": 0}, {"spacing": -10}, {"spacing": "far"}, {"spacing": True}, {"spacing": float("inf")},
])
def test_a_sampling_that_is_not_a_clear_one_is_refused(degrees_grid, sampling):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, ALONG_A_ROW, **sampling)
    assert refused.value.code == "invalid_sampling"


def test_a_whole_number_given_as_a_float_is_a_count(degrees_grid):
    assert len(profile(degrees_grid, ALONG_A_ROW, samples=5.0)["samples"]) == 5


def test_too_many_samples_are_refused_before_anything_is_read(degrees_grid):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, ALONG_A_ROW, spacing=1, max_samples=1000)
    assert refused.value.code == "too_many_samples"
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, ALONG_A_ROW, samples=1001, max_samples=1000)
    assert refused.value.code == "too_many_samples"


def test_a_line_over_the_cell_limit_is_refused_before_it_is_read(degrees_grid):
    with pytest.raises(Refusal) as refused:
        profile(degrees_grid, line((0.5, 9.5), (9.5, 0.5)), samples=10, max_cells=10)
    assert refused.value.code == "too_many_cells"


def test_a_long_profile_is_read(degrees_grid):
    result = profile(degrees_grid, line((0.2, 9.8), (9.8, 0.2)), samples=10_000, resampling="bilinear", max_samples=10_000)
    assert len(result["samples"]) == 10_000
