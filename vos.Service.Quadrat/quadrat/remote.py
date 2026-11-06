import os
import urllib.error
import urllib.request

from .refusal import Refusal

# How GDAL reads a raster at a web address. These are process-wide because the raster library and the statistics
# library each open the address themselves, and both read these from the environment. An operator's own setting wins.
SETTINGS = {
    "GDAL_HTTP_TIMEOUT": "20",
    "GDAL_HTTP_CONNECTTIMEOUT": "5",
    "GDAL_HTTP_MAX_RETRY": "0",
    "GDAL_DISABLE_READDIR_ON_OPEN": "EMPTY_DIR",
    "CPL_VSIL_CURL_ALLOWED_EXTENSIONS": ".tif,.tiff",
}


def configure():
    for name, value in SETTINGS.items():
        os.environ.setdefault(name, value)


def is_address(raster):
    return raster.lower().startswith(("http://", "https://"))


class _Stay(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def confirm_reachable(address):
    """Ask the host for the first byte before the raster library reads, so a redirect or a failure is a stated refusal.

    The raster library follows redirects and offers no way to turn that off, so a host this service is configured
    to read could send it anywhere. A redirect is refused here; a host that starts redirecting between this check
    and the read is the one thing left to the host's own trustworthiness.
    """
    wanted = urllib.request.Request(address, headers={"Range": "bytes=0-0"})
    timeout = float(os.environ["GDAL_HTTP_TIMEOUT"])
    try:
        with urllib.request.build_opener(_Stay).open(wanted, timeout=timeout):
            return
    except urllib.error.HTTPError as answered:
        if 300 <= answered.code < 400:
            raise Refusal("redirected", f"{address} answers with a redirect, which this service does not follow") from None
        raise Refusal("unreadable_raster", f"{address} answered {answered.code}") from None
    except (urllib.error.URLError, TimeoutError, OSError) as unreachable:
        raise Refusal("unreadable_raster", f"{address} could not be reached: {unreachable}") from None
