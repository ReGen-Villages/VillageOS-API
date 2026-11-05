from dataclasses import dataclass

# A zonal read was measured at about 85 bytes of memory per cell of the window it loads: 4 million cells took about
# 0.5 GB and 2.5 seconds, 16 million about 1.4 GB and 9 seconds. These defaults keep one read near half a gigabyte and
# the two that may run at once near a gigabyte.
DEFAULT_MAX_CELLS = 4_000_000
DEFAULT_MAX_CONCURRENT_READS = 2


@dataclass(frozen=True)
class Limits:
    """What one request may ask of this service, set from its own start-up flags and never by a request."""

    max_cells: int = DEFAULT_MAX_CELLS
    max_concurrent_reads: int = DEFAULT_MAX_CONCURRENT_READS

    @classmethod
    def from_args(cls, argv):
        """A flag that is not a positive whole number stops the service from starting, so a typo cannot leave it unlimited."""
        values = {}
        for flag, name in (("--maxCells", "max_cells"), ("--maxConcurrentReads", "max_concurrent_reads")):
            for arg in argv:
                if arg.split("=", 1)[0] == flag:
                    text = arg.split("=", 1)[1] if "=" in arg else ""
                    if not text.isdigit() or int(text) < 1:
                        raise ValueError(f"{flag} is a positive whole number, not {text!r}")
                    values[name] = int(text)
        return cls(**values)
