import base64
import hashlib
import hmac
import json
import time

import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from fastapi import Depends, FastAPI
from fastapi.testclient import TestClient
from jwt.utils import base64url_encode

from vos_service import Config, verifier

THIS_HANDLER = "python-echo-handler"


def verification_key_of(pair) -> str:
    """The public half, encoded the way Mycelium hands it to a daemon."""
    return base64.b64encode(
        pair.public_key().public_bytes(Encoding.DER, PublicFormat.SubjectPublicKeyInfo)
    ).decode()


@pytest.fixture
def pair():
    return ec.generate_private_key(ec.SECP256R1())


@pytest.fixture
def config():
    return Config(port=5103, mycelium_url="https://mycelium.test")


@pytest.fixture
def client(config):
    app = FastAPI()

    @app.post("/protected")
    def protected(_: None = Depends(verifier(config))) -> dict:
        return {"ok": True}

    return TestClient(app)


@pytest.fixture
def signing(config, pair):
    """Enable JWT auth against a stand-in for Mycelium's key pair; yields the private half."""
    config.verification_key = verification_key_of(pair)
    config.issuer = "VillageOS"
    config.audience = THIS_HANDLER
    return pair


def claims_with(**overrides) -> dict:
    now = int(time.time())
    claims = {
        "iss": "VillageOS",
        "aud": THIS_HANDLER,
        "sub": "mycelium",
        "vos:token_type": "mycelium_request",
        "iat": now,
        "nbf": now,
        "exp": now + 60,
    }
    claims.update(overrides)
    return claims


def make_token(pair, **overrides) -> str:
    """Signs the way Mycelium does."""
    return jwt.encode(claims_with(**overrides), pair, algorithm="ES256")


def forged_from_the_verification_key(pair, **overrides) -> str:
    """What someone who reads the verification key off a daemon can produce.

    Signed by hand: pyjwt refuses to use a public key as an HMAC secret, and a forger is not using pyjwt.
    """
    header_and_claims = b".".join(
        base64url_encode(json.dumps(part).encode())
        for part in ({"alg": "HS256", "typ": "JWT"}, claims_with(**overrides))
    )
    signature = hmac.new(
        base64.b64decode(verification_key_of(pair)), header_and_claims, hashlib.sha256
    ).digest()
    return b".".join((header_and_claims, base64url_encode(signature))).decode()


def call(client, token=None):
    headers = {"Authorization": f"Bearer {token}"} if token is not None else {}
    return client.post("/protected", headers=headers)


def test_a_call_with_no_token_passes_when_no_verification_key_was_supplied(client):
    assert call(client).status_code == 200


def test_rejects_a_missing_token_when_auth_is_enabled(client, signing):
    assert call(client).status_code == 401


def test_rejects_a_header_that_is_not_a_bearer_token(client, signing):
    assert client.post("/protected", headers={"Authorization": "Basic abc"}).status_code == 401


def test_accepts_a_valid_token(client, signing):
    assert call(client, make_token(signing)).status_code == 200


def test_accepts_a_token_a_few_seconds_past_its_expiry(client, signing):
    assert call(client, make_token(signing, exp=int(time.time()) - 20)).status_code == 200


def test_rejects_a_tampered_token(client, signing):
    assert call(client, make_token(signing) + "x").status_code == 401


def test_rejects_an_expired_token(client, signing):
    assert call(client, make_token(signing, exp=int(time.time()) - 120)).status_code == 401


def test_rejects_the_wrong_issuer(client, signing):
    assert call(client, make_token(signing, iss="Attacker")).status_code == 401


def test_rejects_a_token_addressed_to_another_service(client, signing):
    assert call(client, make_token(signing, aud="spring-handler")).status_code == 401


def test_rejects_a_persons_browser_token(client, signing):
    assert call(client, make_token(signing, aud="VosClients")).status_code == 401


def test_rejects_a_token_signed_by_another_key(client, signing):
    stranger = ec.generate_private_key(ec.SECP256R1())
    assert call(client, make_token(stranger)).status_code == 401


def test_rejects_the_verification_key_used_as_a_shared_secret(client, signing):
    """Every handler holds this key; accepting it would hand an attacker a signing key."""
    assert call(client, forged_from_the_verification_key(signing)).status_code == 401


def test_rejects_a_token_carrying_no_signature(client, signing):
    token = jwt.encode(claims_with(), key=None, algorithm="none")
    assert call(client, token).status_code == 401


def test_reads_the_config_at_the_time_of_the_call(client, config, signing):
    assert call(client).status_code == 401
    config.verification_key = None
    assert call(client).status_code == 200
