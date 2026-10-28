from __future__ import annotations

import os
from collections.abc import Mapping
from dataclasses import dataclass


@dataclass
class Config:
    port: int = 0
    mycelium_url: str = ""
    token: str | None = None
    api_key: str | None = None
    verification_key: str | None = None
    issuer: str = ""
    #: This service's own name. A token addressed to anything else is refused.
    audience: str = ""


def parse_args(argv: list[str], environment: Mapping[str, str] = os.environ) -> Config | None:
    """Parse the standard --key=value flags.

    A credential is read from the environment alone. A command line is visible to every process
    on the host and is recorded by anything that logs the line a service was started with.
    """
    cfg = Config(
        token=environment.get("Token"), api_key=environment.get("ApiKey"),
        verification_key=environment.get("VerificationKey"))
    seen_port = seen_url = False
    for arg in argv:
        if "=" not in arg:
            continue
        key, value = arg.split("=", 1)
        if key == "--port":
            if not value.isdigit() or not (1 <= int(value) <= 65535):
                return None
            cfg.port, seen_port = int(value), True
        elif key == "--myceliumUrl":
            cfg.mycelium_url, seen_url = value.rstrip("/"), True
        elif key == "--issuer" and value:
            cfg.issuer = value
        elif key == "--audience" and value:
            cfg.audience = value
    if not (seen_port and seen_url):
        return None
    # No default issuer or recipient name to fall back on. Each handler is addressed by its own
    # name, so a shared default could not be correct for anyone and would refuse every call.
    if cfg.verification_key and not (cfg.issuer and cfg.audience):
        return None
    return cfg


def usage(program: str) -> str:
    return (
        f"Usage: .venv/bin/python {program} --port=<port> --myceliumUrl=<url> "
        "[--issuer=<iss>] [--audience=<aud>]\n"
        "--issuer and --audience are required whenever a VerificationKey is set; --audience is this "
        "service's own name.\n"
        "Credentials come from the environment, never the command line: ApiKey, Token, VerificationKey"
    )
