from __future__ import annotations

import base64

import jwt
from cryptography.hazmat.primitives.serialization import load_der_public_key
from fastapi import HTTPException, Request

from .config import Config


def verifier(config: Config):
    """A FastAPI dependency validating a Bearer JWT against `config`.

    No-op when no verification key was supplied (matches the .NET handlers). Issuer, recipient
    name and expiry are validated with 30s clock skew, mirroring ServiceTokenValidator. `config`
    is read on each call, so a key set after the service started is honoured.

    The accepted algorithm is named rather than taken from the token. A checker that honoured the
    token's own claim would accept a token signed with this public key used as a plain shared
    secret, which is a value every handler holds.
    """

    def verify_request(request: Request) -> None:
        if not config.verification_key:
            return
        auth = request.headers.get("authorization", "")
        if not auth.startswith("Bearer "):
            raise HTTPException(status_code=401, detail="missing bearer token")
        try:
            jwt.decode(
                auth[len("Bearer "):],
                key=load_der_public_key(base64.b64decode(config.verification_key)),
                algorithms=["ES256"],
                issuer=config.issuer,
                audience=config.audience,
                leeway=30,
            )
        except jwt.PyJWTError as exc:
            raise HTTPException(status_code=401, detail=str(exc)) from exc

    return verify_request
