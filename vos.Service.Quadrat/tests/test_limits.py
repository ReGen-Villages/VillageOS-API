import pytest

from quadrat.limits import Limits


def test_the_defaults_rest_on_what_a_read_was_measured_to_take():
    limits = Limits()
    assert limits.max_cells == 4_000_000
    assert limits.max_concurrent_reads == 2
    assert limits.max_ticket_bytes == 256 * 1024 * 1024


def test_the_limits_are_read_from_the_services_own_flags():
    limits = Limits.from_args(["--port=5110", "--maxCells=1000", "--maxConcurrentReads=3", "--maxTicketBytes=5000", "--rasterDirectory=/data"])
    assert (limits.max_cells, limits.max_concurrent_reads, limits.max_ticket_bytes) == (1000, 3, 5000)


def test_no_flags_leave_the_defaults():
    assert Limits.from_args(["--port=5110"]) == Limits()


@pytest.mark.parametrize("flag", [
    "--maxCells=0", "--maxCells=-5", "--maxCells=abc", "--maxCells=1.5", "--maxCells=",
    "--maxConcurrentReads=0", "--maxConcurrentReads=two", "--maxTicketBytes=0", "--maxTicketBytes=big",
])
def test_a_limit_that_is_not_a_positive_whole_number_stops_the_service_from_starting_and_says_which(flag):
    with pytest.raises(ValueError, match=flag.split("=")[0]):
        Limits.from_args([flag])
