import os
import re
from urllib.parse import unquote, urlsplit

from .refusal import Refusal

WEB = ("http", "https")
# The raster library reads more than files and web addresses: its virtual file systems (/vsicurl/, /vsizip/),
# URL schemes of its own (s3://, zip://) and driver prefixes (NETCDF:"x") each name a source of their own.
# A drive letter is one character before the colon; a driver or scheme is longer.
NOT_A_PLAIN_NAME = re.compile(r"^(/vsi|[A-Za-z][A-Za-z0-9_+.-]+:)", re.IGNORECASE)


class Sources:
    """Where this service may read a raster from: folders and web addresses it is configured with, and nothing else.

    A request names a raster but never decides where the service may look; with no source configured,
    no raster is read.
    """

    def __init__(self, directories=(), urls=()):
        self.directories = list(directories)
        self.urls = list(urls)

    @classmethod
    def from_args(cls, argv):
        flags = (arg.split("=", 1) for arg in argv if "=" in arg)
        flags = [(key, value) for key, value in flags if value]
        return cls(
            directories=[value for key, value in flags if key == "--rasterDirectory"],
            urls=[value for key, value in flags if key == "--rasterUrl"],
        )

    def permit(self, raster):
        """The path or address to open: the real path of an allowed file, or an allowed web address as given."""
        if not isinstance(raster, str) or not raster or "\0" in raster:
            raise self._refuse("a raster is named by text: a file path or a web address")
        if raster.lower().startswith(tuple(f"{scheme}://" for scheme in WEB)):
            return self._permit_address(raster)
        if NOT_A_PLAIN_NAME.match(raster) or "://" in raster:
            raise self._refuse("only a file path or an http(s) address is read, not a virtual file system or a driver prefix")
        return self._permit_path(raster)

    def _permit_path(self, raster):
        real = os.path.normcase(os.path.realpath(raster))
        for directory in self.directories:
            root = os.path.normcase(os.path.realpath(directory))
            if _is_within(real, root):
                return os.path.realpath(raster)
        raise self._refuse("that file is not under a folder this service is configured to read")

    def _permit_address(self, raster):
        wanted = urlsplit(raster)
        segments = unquote(wanted.path).split("/")
        if wanted.username is not None or wanted.password is not None or ".." in segments or "\\" in raster:
            raise self._refuse("that address is not one this service is configured to read")
        for prefix in self.urls:
            allowed = urlsplit(prefix)
            folder = allowed.path if allowed.path.endswith("/") else allowed.path + "/"
            if (wanted.scheme.lower(), wanted.netloc.lower()) == (allowed.scheme.lower(), allowed.netloc.lower()) and wanted.path.startswith(folder):
                return raster
        raise self._refuse("that address is not one this service is configured to read")

    @staticmethod
    def _refuse(reason):
        return Refusal("source_not_allowed", reason)


def _is_within(path, root):
    if os.path.splitdrive(path)[0] != os.path.splitdrive(root)[0]:
        return False
    return os.path.commonpath([path, root]) == root
