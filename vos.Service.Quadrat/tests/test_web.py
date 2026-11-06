import pytest

from quadrat import describe, sample_point, zonal_stats
from conftest import square



def test_describing_a_raster_over_the_web_fetches_a_little_of_it(big_cog, web_host):
    result = describe(big_cog)
    assert result["width"] == 2048
    assert web_host.bytes_served() < 500_000


def test_a_point_over_the_web_fetches_a_block_not_the_file(big_cog, web_host):
    sample_point(big_cog, 5.5, 5.5, "nearest")
    assert web_host.bytes_served() < 500_000


def test_a_zone_over_the_web_fetches_its_blocks_not_the_file(big_cog, web_host):
    result = zonal_stats(big_cog, square(1, 1, 2, 2))
    assert result["count"] > 0
    assert web_host.bytes_served() < (web_host.folder / "big.tif").stat().st_size / 4


def ask_over_the_web(web_host, name, operation="metadata", **inputs):
    from quadrat import Limits, Sources
    from quadrat.operations import run_operation
    return run_operation({"operation": operation}, {"raster": f"{web_host.base}/{name}", **inputs}, Sources(urls=[f"{web_host.base}/"]), Limits())


def test_a_raster_over_the_web_is_read_through_the_service_operations(big_cog, web_host):
    assert ask_over_the_web(web_host, "big.tif")["width"] == 2048


def test_a_redirect_is_refused_rather_than_followed(big_cog, web_host):
    from quadrat import Refusal
    with pytest.raises(Refusal) as refused:
        ask_over_the_web(web_host, "__redirect/big.tif")
    assert refused.value.code == "redirected"
    assert [request["path"] for request in web_host.requests()] == []


def test_a_missing_file_is_an_unreadable_raster(big_cog, web_host):
    from quadrat import Refusal
    with pytest.raises(Refusal) as refused:
        ask_over_the_web(web_host, "nothing-here.tif")
    assert refused.value.code == "unreadable_raster"


def test_a_host_that_does_not_answer_in_time_is_a_refusal_not_a_hang(big_cog, web_host, monkeypatch):
    monkeypatch.setenv("GDAL_HTTP_TIMEOUT", "1")
    import time
    from quadrat import Refusal
    started = time.monotonic()
    with pytest.raises(Refusal) as refused:
        ask_over_the_web(web_host, "__slow/big.tif")
    assert refused.value.code == "unreadable_raster"
    assert time.monotonic() - started < 2.5


def test_the_raster_library_alone_also_gives_up_on_a_host_that_does_not_answer(big_cog, web_host, monkeypatch):
    import time
    from quadrat import Refusal
    monkeypatch.setenv("GDAL_HTTP_TIMEOUT", "1")
    started = time.monotonic()
    with pytest.raises(Refusal):
        describe(f"{web_host.base}/__slow/big.tif")
    assert time.monotonic() - started < 2.5


def test_only_a_tiff_is_opened_over_the_web(big_cog, web_host):
    from quadrat import Refusal
    (web_host.folder / "notes.txt").write_text("not a raster")
    with pytest.raises(Refusal) as refused:
        ask_over_the_web(web_host, "notes.txt")
    assert refused.value.code == "unreadable_raster"
    assert not [request for request in web_host.requests() if request["method"] == "GET" and request["range"] != "bytes=0-0"]
