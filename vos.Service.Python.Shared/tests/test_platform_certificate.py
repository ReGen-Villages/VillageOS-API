"""A platform presenting a certificate this machine does not trust: what answers there may not be the
platform, so neither the key nor a token is sent to it."""

import asyncio
import ssl
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

import httpx
import pytest
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import NameOID

import vos_service.mycelium as mycelium_module
from vos_service import Config, MyceliumClient


@pytest.fixture
def untrusted_platform(tmp_path):
    """A local HTTPS server with a self-signed certificate; yields (config, what-it-received)."""
    key = ec.generate_private_key(ec.SECP256R1())
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "localhost")])
    certificate = (
        x509.CertificateBuilder()
        .subject_name(name).issuer_name(name).public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.now(timezone.utc) - timedelta(minutes=1))
        .not_valid_after(datetime.now(timezone.utc) + timedelta(hours=1))
        .add_extension(x509.SubjectAlternativeName([x509.DNSName("localhost")]), critical=False)
        .sign(key, hashes.SHA256())
    )
    (tmp_path / "certificate.pem").write_bytes(certificate.public_bytes(serialization.Encoding.PEM))
    (tmp_path / "key.pem").write_bytes(key.private_bytes(
        serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))

    received = []

    class Answering(BaseHTTPRequestHandler):
        def do_POST(self):
            received.append((self.path, self.headers.get("X-API-Key")))
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b'{"token": "never"}')

        def log_message(self, *_):
            pass

    server = HTTPServer(("localhost", 0), Answering)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(tmp_path / "certificate.pem", tmp_path / "key.pem")
    server.socket = context.wrap_socket(server.socket, server_side=True)
    threading.Thread(target=server.serve_forever, daemon=True).start()

    config = Config(port=5103, mycelium_url=f"https://localhost:{server.server_address[1]}")
    yield config, received
    server.shutdown()
    server.server_close()


@pytest.mark.parametrize("call", [
    lambda mycelium: mycelium.register("Example", "h-1"),
    lambda mycelium: mycelium.set_fact("t1", "status", "active"),
    lambda mycelium: mycelium.unsubscribe("s-1"),
], ids=["registration", "a write", "an unsubscribe"])
def test_a_token_is_not_sent_to_a_platform_whose_certificate_the_machine_does_not_trust(untrusted_platform, call):
    config, received = untrusted_platform
    config.token = "a-launch-token"

    with pytest.raises(httpx.ConnectError, match="(?i)certificate"):
        asyncio.run(call(MyceliumClient(config)))

    assert received == [], "the token reached a server whose certificate nothing vouches for"


def test_a_key_is_not_sent_to_a_platform_whose_certificate_the_machine_does_not_trust(untrusted_platform):
    config, received = untrusted_platform
    config.api_key = "vos_ak_example"

    with pytest.raises(httpx.ConnectError, match="(?i)certificate"):
        asyncio.run(MyceliumClient(config).token())

    assert received == [], "the key reached a server whose certificate nothing vouches for"


# What a machine trusts cannot be changed from a test, so this holds the client to asking the
# machine: the list httpx ships with does not hold the development certificate a local platform presents.
def test_calls_to_the_platform_check_its_certificate_against_what_the_machine_trusts(monkeypatch):
    built_with = {}
    monkeypatch.setattr(mycelium_module.httpx, "AsyncClient", lambda **settings: built_with.update(settings))

    mycelium_module.platform_client(5.0)

    trust = built_with["verify"]
    assert isinstance(trust, mycelium_module.truststore.SSLContext)
    assert trust.verify_mode == ssl.CERT_REQUIRED
    assert trust.check_hostname
