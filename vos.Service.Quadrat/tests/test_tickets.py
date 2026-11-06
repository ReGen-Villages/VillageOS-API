"""A raster named by a store ticket: the bytes come from the platform's asset store, never from a path or an address."""

import shutil
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient
from vos_service import AssetTooLarge, AssetUnavailable

import app as appmod
from app import app, host
from conftest import write_png
from quadrat.limits import Limits
from quadrat.pool import ReadPool
from quadrat.sources import Sources

RUN = {"runId": "6d2b0c1e-0000-4000-8000-000000000001", "nodeId": "6d2b0c1e-0000-4000-8000-000000000002"}
TICKET = "sha256:" + "ab" * 32


@pytest.fixture(autouse=True)
def configured(monkeypatch):
    """A service with no source configured: a ticket must need none."""
    pool = ReadPool(2, executor_factory=lambda: ThreadPoolExecutor(2))
    monkeypatch.setattr(appmod, "pool", pool)
    monkeypatch.setattr(appmod, "sources", Sources())
    yield
    pool.close()


class Store:
    """Stands in for the asset store: `fetch_asset` writes the file it was given, or raises what the store would."""

    def __init__(self, holds=None, raises=None):
        self.holds = holds
        self.raises = raises
        self.asked = []
        self.destinations = []

    async def fetch_asset(self, ticket, destination, *, max_bytes, client=None):
        self.asked.append((ticket, max_bytes))
        self.destinations.append(Path(destination))
        if self.raises:
            raise self.raises
        shutil.copyfile(self.holds, destination)
        return "image/tiff"


@pytest.fixture
def store(degrees_grid):
    return Store(holds=degrees_grid)


@pytest.fixture
def client(store):
    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)), \
            patch.object(host.mycelium, "fetch_asset", new=store.fetch_asset):
        with TestClient(app) as c:
            yield c


def run_node(client, params, inputs):
    return client.post("/handle", json={**RUN, "params": params, "inputs": inputs}).json()


def test_a_raster_named_by_a_ticket_is_read_from_the_store(client, store):
    body = run_node(client, {"operation": "point"}, {"raster": TICKET, "lon": 2.5, "lat": 7.5})
    assert body["result"] == "success", body
    assert body["outputs"]["result"]["value"] == 22
    assert store.asked[0][0] == TICKET


def test_a_zone_and_a_description_are_read_from_a_ticket_too(client):
    assert run_node(client, {"operation": "metadata"}, {"raster": TICKET})["outputs"]["result"]["width"] == 10
    zonal = run_node(client, {"operation": "zonal"}, {"raster": TICKET, "bbox": [2, 6, 4, 8]})
    assert zonal["outputs"]["result"]["count"] == 4


def test_a_terrain_tile_is_read_from_a_ticket(tmp_path, monkeypatch):
    png = write_png(tmp_path / "tile.png", [[(135, 170, 128), (128, 10, 0)], [(128, 20, 0), (128, 30, 0)]])
    held = Store(holds=png)
    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)), \
            patch.object(host.mycelium, "fetch_asset", new=held.fetch_asset), TestClient(app) as c:
        body = run_node(c, {"operation": "point", "encoding": "terrarium"}, {"raster": TICKET, "tile": {"z": 1, "x": 1, "y": 0}, "lon": 10, "lat": 80})
    assert body["outputs"]["result"]["value"] == 1962.5


def test_the_bytes_are_not_kept_after_the_answer(client, store):
    run_node(client, {"operation": "metadata"}, {"raster": TICKET})
    assert store.destinations and not store.destinations[0].exists()
    assert not store.destinations[0].parent.exists()


def test_the_bytes_are_not_kept_when_the_read_is_refused(client, store):
    body = run_node(client, {"operation": "zonal"}, {"raster": TICKET, "bbox": [50, 50, 60, 60]})
    assert body["error"].startswith("outside_raster:")
    assert not store.destinations[0].parent.exists()


@pytest.mark.parametrize("named", ["sha256:zz", "sha256:" + "AB" * 32, "sha256:" + "ab" * 31, "SHA256:" + "ab" * 32 + "/../x"])
def test_something_that_looks_like_a_ticket_but_is_not_one_is_refused_without_asking_the_store(client, store, named):
    body = run_node(client, {"operation": "metadata"}, {"raster": named})
    assert body["error"].startswith("invalid_ticket:")
    assert store.asked == []


@pytest.mark.parametrize("raises,code", [
    (AssetUnavailable(404, TICKET), "ticket_not_found"),
    (AssetUnavailable(403, TICKET), "ticket_unreadable"),
    (AssetUnavailable(500, TICKET), "ticket_unreadable"),
    (AssetTooLarge(TICKET, 10, 99), "ticket_too_large"),
])
def test_a_ticket_the_store_will_not_give_is_a_stated_refusal(raises, code, degrees_grid):
    refused = Store(holds=degrees_grid, raises=raises)
    with patch.object(host.mycelium, "register", new=AsyncMock(return_value=True)), \
            patch.object(host.mycelium, "fetch_asset", new=refused.fetch_asset), TestClient(app) as c:
        body = run_node(c, {"operation": "metadata"}, {"raster": TICKET})
    assert body["result"] == "fail" and body["error"].startswith(f"{code}:"), body


def test_the_most_a_ticket_may_hold_is_the_limit_and_is_passed_to_the_store(client, store, monkeypatch):
    monkeypatch.setattr(appmod, "limits", Limits(max_ticket_bytes=12345))
    run_node(client, {"operation": "metadata"}, {"raster": TICKET})
    assert store.asked[0][1] == 12345


def test_the_folder_a_ticket_is_held_in_is_not_a_source_for_anything_else(client, store, tmp_path):
    run_node(client, {"operation": "metadata"}, {"raster": TICKET})
    held_in = store.destinations[0].parent
    body = run_node(client, {"operation": "metadata"}, {"raster": str(held_in / "asset")})
    assert body["error"].startswith("source_not_allowed:")
