import json
import subprocess
import sys
from pathlib import Path

from conftest import square

SERVICE_FOLDER = Path(__file__).resolve().parent.parent


def run(*args):
    return subprocess.run([sys.executable, "-m", "quadrat", *args], capture_output=True, text=True, cwd=SERVICE_FOLDER)


def test_metadata_prints_json(degrees_grid):
    done = run("metadata", degrees_grid)
    assert done.returncode == 0
    assert json.loads(done.stdout)["width"] == 10


def test_point_prints_the_value(degrees_grid):
    done = run("point", degrees_grid, "--lon", "2.5", "--lat", "7.5")
    assert done.returncode == 0
    assert json.loads(done.stdout)["value"] == 22.0


def test_zonal_reads_a_bbox(degrees_grid):
    done = run("zonal", degrees_grid, "--bbox", "2", "6", "4", "8")
    assert done.returncode == 0
    assert json.loads(done.stdout)["count"] == 4


def test_zonal_reads_a_geojson_file(degrees_grid, tmp_path):
    geometry = tmp_path / "zone.geojson"
    geometry.write_text(json.dumps(square(2, 6, 4, 8)))
    done = run("zonal", degrees_grid, "--geometry", str(geometry))
    assert json.loads(done.stdout)["count"] == 4


def test_a_refusal_prints_its_code_and_exits_2(degrees_grid):
    done = run("point", degrees_grid, "--lon", "50", "--lat", "50")
    assert done.returncode == 2
    assert json.loads(done.stdout)["refused"] == "outside_raster"


def test_an_inverted_bbox_is_refused_by_the_command_line(degrees_grid):
    done = run("zonal", degrees_grid, "--bbox", "4", "6", "2", "8")
    assert done.returncode == 2
    assert json.loads(done.stdout)["refused"] == "invalid_bbox"
