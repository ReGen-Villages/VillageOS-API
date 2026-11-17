"""The live smoke test, tested: a real Quadrat process answering real HTTP, run through smoke.py.

smoke.py is what is pointed at a running service to see that it reads a real raster correctly (the Tatra elevation
tile in smoke_tatra.json). This file proves the script itself against a service started here on a raster whose
values are known, so a script that always said "pass" would fail these tests.
"""
import json
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

import pytest

import smoke

SERVICE = Path(__file__).resolve().parents[1]


@pytest.fixture
def running_service(tmp_path, degrees_grid):
    """Quadrat started as an operator would start it, reading only the folder the raster is in."""
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    folder = Path(degrees_grid).parent
    process = subprocess.Popen(
        [sys.executable, "app.py", f"--port={port}", "--myceliumUrl=http://127.0.0.1:9", f"--rasterDirectory={folder}"],
        cwd=SERVICE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    url = f"http://127.0.0.1:{port}"
    try:
        for _ in range(100):
            try:
                urllib.request.urlopen(url + "/health", timeout=1).read()
                break
            except OSError:
                if process.poll() is not None:
                    pytest.fail("the service exited before it answered /health")
                time.sleep(0.2)
        else:
            pytest.fail("the service did not answer /health in 20 seconds")
        yield url
    finally:
        process.kill()
        process.wait()


def checks_for(raster):
    """What a read of degrees_grid (value r*10+c over lon 0..10, lat 0..10) must answer."""
    return [
        {"name": "the raster is described", "operation": "metadata", "inputs": {"raster": raster},
         "expect": {"outputs.result.width": 10, "outputs.result.height": 10}},
        {"name": "a point reads the cell under it", "operation": "point",
         "inputs": {"raster": raster, "lon": 2.5, "lat": 7.5}, "expect": {"outputs.result.value": [22.0, 0.001]}},
        {"name": "a point between cell centres is interpolated", "operation": "point", "params": {"resampling": "bilinear"},
         "inputs": {"raster": raster, "lon": 3.0, "lat": 7.5}, "expect": {"outputs.result.value": [22.5, 0.001]}},
        {"name": "a zone is summarised", "operation": "zonal",
         "inputs": {"raster": raster, "bbox": [0, 8, 2, 10]}, "expect": {"outputs.result.min": [0, 0.001], "outputs.result.max": [11, 0.001]}},
        {"name": "a line is read at its samples", "operation": "profile",
         "inputs": {"raster": raster, "line": {"type": "LineString", "coordinates": [[0.5, 7.5], [4.5, 7.5]]}, "samples": 5},
         "expect": {"outputs.result.samples.0.value": [20, 0.001], "outputs.result.samples.4.value": [24, 0.001], "outputs.result.outside_samples": 0}},
        {"name": "a point off the raster is refused with its code", "operation": "point",
         "inputs": {"raster": raster, "lon": 50, "lat": 50}, "fails": "outside_raster"},
    ]


def test_a_service_that_reads_correctly_passes_every_check(running_service, degrees_grid):
    results = smoke.run(running_service, checks_for(degrees_grid))
    assert [(name, ok) for name, ok, _ in results[2:]] == [(check["name"], True) for check in checks_for(degrees_grid)], results


def test_a_raster_named_without_a_folder_is_looked_for_in_the_folder_given(running_service, degrees_grid):
    check = {"name": "by name", "operation": "metadata", "inputs": {"raster": Path(degrees_grid).name}, "expect": {"outputs.result.width": 10}}
    [*_, (_, ok, detail)] = smoke.run(running_service, [check], rasters=Path(degrees_grid).parent)
    assert ok, detail


def test_an_address_is_left_as_it_is_when_a_folder_is_given(running_service):
    check = {"name": "address", "operation": "metadata", "inputs": {"raster": "http://example.invalid/a.tif"}, "fails": "source_not_allowed"}
    [*_, (_, ok, detail)] = smoke.run(running_service, [check], rasters="/some/folder")
    assert ok, detail

def test_the_service_is_asked_whether_it_is_healthy_and_what_it_offers_before_any_read(running_service):
    results = smoke.run(running_service, [])
    assert [name for name, _, _ in results] == ["the service is healthy", "the manifest names the service and its operations"]
    assert all(ok for _, ok, _ in results)


def test_a_value_that_is_not_the_one_expected_fails_the_check_and_says_which(running_service, degrees_grid):
    wrong = checks_for(degrees_grid)[1]
    wrong["expect"] = {"outputs.result.value": [23.0, 0.001]}
    [*_, (name, ok, detail)] = smoke.run(running_service, [wrong])
    assert not ok
    assert "outputs.result.value" in detail and "23.0" in detail and "22.0" in detail


def test_a_refusal_where_a_read_was_expected_fails_the_check_with_the_refusal(running_service, degrees_grid):
    off = {"name": "off", "operation": "point", "inputs": {"raster": degrees_grid, "lon": 50, "lat": 50}, "expect": {"outputs.result.value": 1}}
    [*_, (_, ok, detail)] = smoke.run(running_service, [off])
    assert not ok and "outside_raster" in detail


def test_an_answer_where_a_refusal_was_expected_fails_the_check(running_service, degrees_grid):
    fine = {"name": "fine", "operation": "metadata", "inputs": {"raster": degrees_grid}, "fails": "outside_raster"}
    [*_, (_, ok, detail)] = smoke.run(running_service, [fine])
    assert not ok and "outside_raster" in detail


def test_a_different_refusal_than_the_one_expected_fails_the_check(running_service, degrees_grid):
    off = {"name": "off", "operation": "point", "inputs": {"raster": degrees_grid, "lon": 50, "lat": 50}, "fails": "invalid_geometry"}
    [*_, (_, ok, detail)] = smoke.run(running_service, [off])
    assert not ok and "invalid_geometry" in detail and "outside_raster" in detail


def test_a_service_that_is_not_there_fails_instead_of_raising(degrees_grid):
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
    results = smoke.run(f"http://127.0.0.1:{port}", checks_for(degrees_grid))
    assert results and not results[0][1]
    assert len(results) == 1, "nothing further is tried once the service does not answer"


def test_the_command_line_exits_0_when_every_check_passes_and_1_when_one_fails(running_service, degrees_grid, tmp_path):
    good, bad = tmp_path / "good.json", tmp_path / "bad.json"
    good.write_text(json.dumps(checks_for(degrees_grid)))
    wrong = checks_for(degrees_grid)
    wrong[1]["expect"] = {"outputs.result.value": 99}
    bad.write_text(json.dumps(wrong))

    def run(checks):
        return subprocess.run([sys.executable, "smoke.py", "--url", running_service, "--checks", str(checks)],
                              cwd=SERVICE, capture_output=True, text=True)

    ok, failed = run(good), run(bad)
    assert ok.returncode == 0 and "FAIL" not in ok.stdout
    assert failed.returncode == 1 and "FAIL" in failed.stdout and "a point reads the cell under it" in failed.stdout


def test_the_checks_shipped_for_the_tatra_tile_are_well_formed():
    checks = json.loads((SERVICE / "smoke_tatra.json").read_text(encoding="utf-8"))
    assert {check["operation"] for check in checks} >= {"metadata", "point", "zonal"}
    for check in checks:
        assert check["name"] and check["inputs"]["raster"]
        assert ("expect" in check) != ("fails" in check), check["name"]
