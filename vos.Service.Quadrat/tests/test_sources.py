import os
import sys

import pytest

from quadrat import Refusal
from quadrat.sources import Sources


def link_directory(target, link):
    try:
        os.symlink(target, link, target_is_directory=True)
    except OSError:
        if sys.platform != "win32":
            raise
        import _winapi

        _winapi.CreateJunction(str(target), str(link))


@pytest.fixture
def folders(tmp_path):
    allowed = tmp_path / "rasters"
    outside = tmp_path / "private"
    (allowed / "nested").mkdir(parents=True)
    outside.mkdir()
    (allowed / "dem.tif").write_bytes(b"")
    (allowed / "nested" / "dem.tif").write_bytes(b"")
    (outside / "secret.tif").write_bytes(b"")
    return allowed, outside


def refused(sources, raster):
    with pytest.raises(Refusal) as refusal:
        sources.permit(raster)
    assert refusal.value.code == "source_not_allowed"
    return refusal.value


def test_a_file_inside_an_allowed_folder_is_permitted(folders):
    allowed, _ = folders
    assert Sources(directories=[str(allowed)]).permit(str(allowed / "dem.tif")) == os.path.realpath(allowed / "dem.tif")


def test_a_file_in_a_folder_below_an_allowed_one_is_permitted(folders):
    allowed, _ = folders
    assert Sources(directories=[str(allowed)]).permit(str(allowed / "nested" / "dem.tif"))


def test_a_file_outside_the_allowed_folders_is_refused(folders):
    allowed, outside = folders
    refused(Sources(directories=[str(allowed)]), str(outside / "secret.tif"))


def test_a_path_that_climbs_out_with_dot_dot_is_refused(folders):
    allowed, _ = folders
    refused(Sources(directories=[str(allowed)]), str(allowed / "nested" / ".." / ".." / "private" / "secret.tif"))


def test_a_folder_whose_name_only_begins_like_an_allowed_one_is_refused(tmp_path):
    allowed, lookalike = tmp_path / "rasters", tmp_path / "rasters-private"
    allowed.mkdir()
    lookalike.mkdir()
    (lookalike / "secret.tif").write_bytes(b"")
    refused(Sources(directories=[str(allowed)]), str(lookalike / "secret.tif"))


def test_a_link_inside_an_allowed_folder_that_leads_outside_is_refused(folders):
    allowed, outside = folders
    link_directory(outside, allowed / "shortcut")
    refused(Sources(directories=[str(allowed)]), str(allowed / "shortcut" / "secret.tif"))


def test_nothing_is_permitted_until_a_source_is_configured(folders):
    allowed, _ = folders
    refused(Sources(), str(allowed / "dem.tif"))
    refused(Sources(), "https://data.example.org/dem.tif")


def test_a_web_address_is_refused_unless_it_is_allowed(folders):
    allowed, _ = folders
    refused(Sources(directories=[str(allowed)]), "https://data.example.org/dem.tif")


ALLOWED_ADDRESS = "https://data.example.org/rasters/"


def test_a_web_address_under_an_allowed_address_is_permitted():
    sources = Sources(urls=[ALLOWED_ADDRESS])
    assert sources.permit("https://data.example.org/rasters/z10/dem.tif") == "https://data.example.org/rasters/z10/dem.tif"


@pytest.mark.parametrize("address", [
    "https://data.example.org/other/dem.tif",
    "https://data.example.org.evil.test/rasters/dem.tif",
    "https://data.example.org@evil.test/rasters/dem.tif",
    "https://evil.test/rasters/dem.tif",
    "http://data.example.org/rasters/dem.tif",
    "https://data.example.org:8443/rasters/dem.tif",
    "https://data.example.org/rasters/../private/dem.tif",
    "https://data.example.org/rasters/%2e%2e/private/dem.tif",
    "https://data.example.org/rasters-private/dem.tif",
])
def test_an_address_that_only_looks_like_an_allowed_one_is_refused(address):
    refused(Sources(urls=[ALLOWED_ADDRESS]), address)


@pytest.mark.parametrize("raster", [
    "/vsicurl/https://data.example.org/rasters/dem.tif",
    "/vsizip/archive.zip/dem.tif",
    "/vsimem/dem.tif",
    "zip://archive.zip!dem.tif",
    "file:///etc/passwd",
    "s3://bucket/dem.tif",
    "gs://bucket/dem.tif",
    'NETCDF:"data.nc":elevation',
    "GTiff:dem.tif",
])
def test_a_virtual_file_system_or_driver_prefix_is_never_permitted(raster, folders):
    allowed, _ = folders
    refused(Sources(directories=[str(allowed)], urls=[ALLOWED_ADDRESS, "s3://bucket/"]), raster)


@pytest.mark.parametrize("raster", [None, 12, ["a.tif"], {"path": "a.tif"}, "", "dem\0.tif"])
def test_a_raster_that_is_not_a_plain_name_is_refused(raster, folders):
    allowed, _ = folders
    refused(Sources(directories=[str(allowed)]), raster)


def test_the_refusal_says_what_would_be_allowed_without_listing_the_folders(folders):
    allowed, outside = folders
    reason = refused(Sources(directories=[str(allowed)]), str(outside / "secret.tif")).reason
    assert "configured" in reason
    assert str(allowed) not in reason


def test_sources_are_read_from_the_services_own_flags():
    sources = Sources.from_args([
        "--port=5110", "--myceliumUrl=https://localhost:7243",
        "--rasterDirectory=/data/a", "--rasterDirectory=/data/b", f"--rasterUrl={ALLOWED_ADDRESS}",
    ])
    assert sources.directories == ["/data/a", "/data/b"]
    assert sources.urls == [ALLOWED_ADDRESS]


def test_no_flags_configure_no_sources():
    sources = Sources.from_args(["--port=5110"])
    assert (sources.directories, sources.urls) == ([], [])
