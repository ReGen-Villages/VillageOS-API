"""What of an answer is recorded on a Thing, and what is refused before anything is read or sent."""
import pytest

from quadrat import Refusal
from quadrat.observations import recorded, requested

THING = "6d2b0c1e-0000-4000-8000-0000000000aa"


def refused(call, *args):
    with pytest.raises(Refusal) as raised:
        call(*args)
    return raised.value.code


def test_nothing_is_requested_when_neither_input_is_given():
    assert requested({"raster": "r.tif"}) is None


def test_a_thing_and_the_properties_to_fill_are_requested_together():
    assert requested({"thingId": THING, "observations": {"elevation_mean_m": "mean"}}) == (THING, {"elevation_mean_m": "mean"})


@pytest.mark.parametrize("inputs", [{"thingId": THING}, {"observations": {"a": "mean"}}])
def test_one_without_the_other_is_refused(inputs):
    assert refused(requested, inputs) == "missing_input"


@pytest.mark.parametrize("thing", ["not-a-guid", 7, ""])
def test_a_thing_that_is_not_a_guid_is_refused(thing):
    assert refused(requested, {"thingId": thing, "observations": {"a": "mean"}}) == "invalid_thing"


@pytest.mark.parametrize("observations", [{}, [], "mean", {"a": 3}, {"a": ""}, {"": "mean"}])
def test_observations_must_name_each_property_and_the_statistic_that_fills_it(observations):
    assert refused(requested, {"thingId": THING, "observations": observations}) == "invalid_observations"


def test_each_property_takes_the_statistic_it_names():
    answer = {"mean": 1285.4, "min": 813, "max": 2132, "count": 200}
    samples, skipped = recorded(answer, {"elevation_mean_m": "mean", "elevation_max_m": "max"})
    assert samples == [{"property": "elevation_mean_m", "value": 1285.4}, {"property": "elevation_max_m", "value": 2132}]
    assert skipped == []


def test_a_statistic_is_found_by_its_path_through_lists_too():
    answer = {"samples": [{"value": 1962.0}, {"value": 1700.5}]}
    samples, _ = recorded(answer, {"start_m": "samples.0.value", "second_m": "samples.1.value"})
    assert [s["value"] for s in samples] == [1962.0, 1700.5]


def test_a_statistic_the_answer_has_as_null_is_left_out_and_said_so():
    samples, skipped = recorded({"mean": None, "min": 3}, {"a": "mean", "b": "min"})
    assert samples == [{"property": "b", "value": 3}]
    assert skipped == ["a"]


def test_a_statistic_the_answer_does_not_have_is_refused():
    assert refused(recorded, {"mean": 1}, {"a": "median"}) == "unknown_statistic"


def test_a_statistic_that_is_not_one_value_is_refused():
    assert refused(recorded, {"unclassified": {"cells": 3}}, {"a": "unclassified"}) == "not_a_reading"
