import re
import shutil
import tempfile
from contextlib import asynccontextmanager
from pathlib import Path

from vos_service import AssetTooLarge, AssetUnavailable

from .refusal import Refusal

TICKET = re.compile(r"^sha256:[0-9a-f]{64}$")


def names_a_ticket(raster):
    """A raster named `sha256:...` is meant as a store ticket; whether it is a well-formed one is `held`'s to say."""
    return isinstance(raster, str) and raster.lower().startswith("sha256:")


@asynccontextmanager
async def held(mycelium, ticket, max_bytes, sources):
    """The bytes a store ticket names, written to a folder of their own that is read from and then removed.

    Yields the path to read and the sources with that folder allowed for this one request. The bytes come from
    the platform's asset store, which is where authentication and provider terms already live.
    """
    if not TICKET.match(ticket):
        raise Refusal("invalid_ticket", "a store ticket is sha256: followed by 64 lowercase hexadecimal digits")
    folder = tempfile.mkdtemp(prefix="quadrat-ticket-")
    try:
        path = Path(folder) / "asset"
        try:
            await mycelium.fetch_asset(ticket, path, max_bytes=max_bytes)
        except AssetTooLarge as large:
            raise Refusal("ticket_too_large", f"{large}: raise --maxTicketBytes to take more") from None
        except AssetUnavailable as unavailable:
            if unavailable.status == 404:
                raise Refusal("ticket_not_found", f"the asset store holds nothing under {ticket}") from None
            raise Refusal("ticket_unreadable", f"the asset store answered {unavailable.status} for {ticket}") from None
        yield str(path), sources.with_directory(folder)
    finally:
        shutil.rmtree(folder, ignore_errors=True)
