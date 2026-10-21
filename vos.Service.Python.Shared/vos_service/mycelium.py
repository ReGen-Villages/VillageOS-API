from __future__ import annotations

import ssl
from collections.abc import Callable
from datetime import datetime, timedelta, timezone
from urllib.parse import quote

import httpx
import jwt
import truststore

from .config import Config

# Every method takes an optional `client`, so a test can inject an httpx.AsyncClient with a MockTransport.
# Write kinds — Facts, Observations, Sediment: docs/SERVICE_CONTRACT.md § "Writing data back".
# Snapshot selector — subscribe to a slice: docs/SERVICE_CONTRACT.md § "Selecting a slice".

#: A held token is exchanged again this long before it runs out.
REPLACEMENT_LEAD = timedelta(seconds=30)


def platform_client(timeout: float) -> httpx.AsyncClient:
    """A client for calls to Mycelium, which carry the key or a token.

    It checks the platform's certificate against what this machine trusts, so a Mycelium on the same
    machine presenting the development certificate is reached once that certificate is trusted, and
    anything else presenting a certificate nothing vouches for is never sent the credential.
    """
    return httpx.AsyncClient(timeout=timeout, verify=truststore.SSLContext(ssl.PROTOCOL_TLS_CLIENT))


def sediment_reading(thing_id: str, prop: str, value, observed_at: str) -> dict:
    """One historical reading, with its Thing under the name the sediment route reads it by."""
    return {"objectId": thing_id, "property": prop, "value": value, "observedAt": observed_at}


def _utc_now() -> datetime:
    return datetime.now(timezone.utc)


class MyceliumClient:
    """A service's calls to the Mycelium gateway. `config` is read on each call."""

    def __init__(self, config: Config, *, now: Callable[[], datetime] = _utc_now):
        self._config = config
        self._now = now
        self._held_token: tuple[str, datetime] | None = None

    async def token(self, *, client: httpx.AsyncClient | None = None) -> str:
        """The bearer for a call to Mycelium.

        A key does not run out, but the token it is exchanged for lasts minutes. So the token is held
        and the exchange repeated shortly before it runs out, rather than on every call. A key is
        presented before a token given beside it, as the C# host does. With neither there is nothing
        to present: Mycelium refuses a token request that carries no key, so none is made.
        """
        if self._config.api_key:
            if self._held_token is None or self._held_token[1] - self._now() <= REPLACEMENT_LEAD:
                self._held_token = await self._exchange_the_key(client)
            return self._held_token[0]
        if self._config.token:
            return self._config.token
        raise RuntimeError("neither ApiKey nor Token is set, so there is no credential to present to Mycelium")

    async def _exchange_the_key(self, client: httpx.AsyncClient | None) -> tuple[str, datetime]:
        url = f"{self._config.mycelium_url}/api/auth/token"
        headers = {"X-API-Key": self._config.api_key}
        if client is not None:
            res = await client.post(url, headers=headers)
        else:
            async with platform_client(timeout=5) as c:
                res = await c.post(url, headers=headers)
        if not res.is_success:
            raise RuntimeError(f"Mycelium refused to exchange the API key ({res.status_code})")
        token = res.json()["token"]
        # Read, not checked: only Mycelium can check its own signature, and it does on every call.
        expires = jwt.decode(token, options={"verify_signature": False}).get("exp")
        if expires is None:
            # It would be held forever and fail only once something depended on it.
            raise RuntimeError("Mycelium exchanged the API key for a token that states no expiry")
        return token, datetime.fromtimestamp(expires, timezone.utc)

    async def register(self, service_name: str, handler_id: str, *, client: httpx.AsyncClient | None = None) -> bool:
        base = f"http://localhost:{self._config.port}"
        payload = {
            "handlerId": handler_id,
            "serviceName": service_name,
            "endpointUrl": base,
            "startCommand": "endpoint-service",
            "stopEndpoint": f"{base}/shutdown",
            "healthEndpoint": f"{base}/health",
        }
        res = await self._post("/api/mycelium/register", payload, client=client, timeout=5)
        return res.is_success

    async def set_fact(self, thing_id: str, prop: str, value, *, client: httpx.AsyncClient | None = None) -> int:
        """Assert a structural Fact; return the commit sequence number (405 if ObservationOnly)."""
        res = await self._post(
            f"/api/things/{thing_id}/properties/{quote(prop, safe='')}/facts", {"value": value}, client=client)
        if res.status_code != 201:
            raise RuntimeError(f"fact write returned {res.status_code}")
        return res.json().get("sequenceNumber", 0)

    async def record_observation(
        self, thing_id: str, prop: str, value, observed_at: str | None = None, *, client: httpx.AsyncClient | None = None
    ) -> None:
        """Record one Observation (202); pass observed_at for late samples, omit for now (405 if FactOnly)."""
        body = {"value": value}
        if observed_at:
            body["observedAt"] = observed_at
        res = await self._post(
            f"/api/things/{thing_id}/properties/{quote(prop, safe='')}/observations", body, client=client)
        if not res.is_success:
            raise RuntimeError(f"observation write returned {res.status_code}")

    async def record_observations(self, thing_id: str, samples: list[dict], *, client: httpx.AsyncClient | None = None) -> int:
        """Record many samples (dicts of {property, value, observedAt?}) in one batch (202); return the accepted count."""
        if not samples:
            return 0
        res = await self._post(f"/api/things/{thing_id}/observations", samples, client=client)
        if not res.is_success:
            raise RuntimeError(f"observation batch returned {res.status_code}")
        return res.json().get("accepted", len(samples))

    async def deposit_sediment(self, readings: list[dict], *, client: httpx.AsyncClient | None = None) -> dict:
        """Bulk-load historical readings (each a `sediment_reading`) to sealed Sapwood (202)."""
        if not readings:
            raise ValueError("at least one reading is required")
        res = await self._post("/api/sediment", readings, client=client)
        if not res.is_success:
            raise RuntimeError(f"sediment deposit returned {res.status_code}")
        return res.json()

    @staticmethod
    def slice_by_type_and_traverse(type_: str, predicate: str) -> dict:
        """A representative slice: every Thing of type_ plus its depth-1 predicate neighbours."""
        return {"types": [type_], "traverse": [{"predicate": predicate, "direction": "outgoing", "depth": 1}]}

    async def subscribe(self, selector: dict, *, client: httpx.AsyncClient | None = None) -> dict:
        """POST the selector to /api/subscriptions; return the resolved snapshot closure."""
        res = await self._post("/api/subscriptions", selector, client=client)
        if not res.is_success:
            raise RuntimeError(f"subscribe returned {res.status_code}")
        return res.json()

    async def unsubscribe(self, subscription_id: str, *, client: httpx.AsyncClient | None = None) -> None:
        """Release a subscription (best-effort)."""
        headers = {"Authorization": f"Bearer {await self.token(client=client)}"}
        url = f"{self._config.mycelium_url}/api/subscriptions/{subscription_id}"
        if client is not None:
            await client.delete(url, headers=headers)
        else:
            async with platform_client(timeout=10) as c:
                await c.delete(url, headers=headers)

    async def _post(self, path: str, json_body, *, client: httpx.AsyncClient | None, timeout: float = 30) -> httpx.Response:
        headers = {"Authorization": f"Bearer {await self.token(client=client)}"}
        url = f"{self._config.mycelium_url}{path}"
        if client is not None:
            return await client.post(url, json=json_body, headers=headers)
        async with platform_client(timeout=timeout) as c:
            return await c.post(url, json=json_body, headers=headers)
