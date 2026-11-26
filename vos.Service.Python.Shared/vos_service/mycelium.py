from __future__ import annotations

import asyncio
import contextlib
import os
import ssl
import sys
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


LONGEST_PAUSE = 300.0


class NotAcceptedYet(RuntimeError):
    """Mycelium did not take the request this time and may take it later: a 503 while it starts, a 408 or a 429."""

    def __init__(self, message: str, retry_after: float | None = None):
        super().__init__(message)
        self.retry_after = retry_after


def pause_after(failures_in_a_row: int, retry_after: float | None = None) -> float:
    """Capped, so a broker that names an hour is asked again within minutes of being ready."""
    if retry_after is not None:
        return min(retry_after, LONGEST_PAUSE)
    return min(LONGEST_PAUSE, 2.0 ** min(max(failures_in_a_row - 1, 0), 30))


def would_be_given_again(status: int) -> bool:
    """408 and 429 say the same request may succeed later; every other 4xx says it will not."""
    return 400 <= status < 500 and status not in (408, 429)


def _retry_after(res: httpx.Response) -> float | None:
    value = res.headers.get("Retry-After", "")
    return float(value) if value.isdigit() else None


def sediment_reading(thing_id: str, prop: str, value, observed_at: str) -> dict:
    """One historical reading, with its Thing under the name the sediment route reads it by."""
    return {"objectId": thing_id, "property": prop, "value": value, "observedAt": observed_at}


class PlatformRefusal(RuntimeError):
    """The platform's own status and words for a slice it refused, so a demo can pass the refusal on as given."""

    def __init__(self, status: int, body: str) -> None:
        super().__init__(f"the platform refused the subscription with {status}: {body}")
        self.status = status
        self.body = body


class AssetUnavailable(RuntimeError):
    """The store would not give the bytes a ticket names; `status` is what it answered."""

    def __init__(self, status: int, ticket: str) -> None:
        super().__init__(f"the asset store answered {status} for {ticket}")
        self.status = status
        self.ticket = ticket


class AssetTooLarge(RuntimeError):
    """The bytes a ticket names are more than the caller will take."""

    def __init__(self, ticket: str, max_bytes: int, size: int | None = None) -> None:
        held = f"{size} bytes" if size is not None else "more than that"
        super().__init__(f"{ticket} holds {held}, and at most {max_bytes} bytes are taken")


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
            refusal = f"Mycelium refused to exchange the API key ({res.status_code})"
            if would_be_given_again(res.status_code):
                raise RuntimeError(refusal)
            raise NotAcceptedYet(refusal, _retry_after(res))
        token = res.json()["token"]
        # Read, not checked: only Mycelium can check its own signature, and it does on every call.
        expires = jwt.decode(token, options={"verify_signature": False}).get("exp")
        if expires is None:
            # It would be held forever and fail only once something depended on it.
            raise RuntimeError("Mycelium exchanged the API key for a token that states no expiry")
        return token, datetime.fromtimestamp(expires, timezone.utc)

    async def register_until_settled(
        self, service_name: str, handler_id: str, *, client: httpx.AsyncClient | None = None, sleep=asyncio.sleep
    ) -> bool:
        asks = 1
        while True:
            try:
                return await self.register(service_name, handler_id, client=client)
            except (NotAcceptedYet, httpx.TransportError) as exc:
                if asks == 1:
                    print(f"mycelium has not taken the registration ({exc}); it is asked again, each pause longer, until it does",
                          file=sys.stderr)
                await sleep(pause_after(asks, getattr(exc, "retry_after", None)))
                asks += 1

    async def register(self, service_name: str, handler_id: str, *, client: httpx.AsyncClient | None = None) -> bool:
        """True once Mycelium takes it, False for an answer it would give again, NotAcceptedYet for one it may not."""
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
        if res.is_success:
            return True
        if would_be_given_again(res.status_code):
            return False
        raise NotAcceptedYet(f"{res.status_code}", _retry_after(res))

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
            raise PlatformRefusal(res.status_code, res.text)
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

    async def thing_properties(self, thing_id, *, client: httpx.AsyncClient | None = None) -> dict:
        """Every property of a Thing as the platform answers them, each as an object holding its `Value`."""
        headers = {"Authorization": f"Bearer {await self.token(client=client)}"}
        url = f"{self._config.mycelium_url}/api/things/{thing_id}/properties"
        if client is not None:
            res = await client.get(url, headers=headers)
        else:
            async with platform_client(timeout=10) as c:
                res = await c.get(url, headers=headers)
        if not res.is_success:
            raise RuntimeError(f"reading the properties of thing {thing_id} returned {res.status_code}")
        return res.json()

    async def fetch_asset(self, ticket: str, destination, *, max_bytes: int, client: httpx.AsyncClient | None = None) -> str:
        """Write the bytes a store ticket names to `destination` and return their content type.

        The bytes are streamed to the file, never held whole, and a ticket larger than `max_bytes` is refused
        (`AssetTooLarge`) by its declared size or as soon as it outgrows the limit, leaving no file.
        """
        headers = {"Authorization": f"Bearer {await self.token(client=client)}"}
        url = f"{self._config.mycelium_url}/api/assets/{quote(ticket, safe=':')}"
        if client is not None:
            return await self._stream_asset(client, url, headers, ticket, destination, max_bytes)
        async with platform_client(timeout=60) as c:
            return await self._stream_asset(c, url, headers, ticket, destination, max_bytes)

    @staticmethod
    async def _stream_asset(client, url, headers, ticket, destination, max_bytes) -> str:
        async with client.stream("GET", url, headers=headers) as res:
            if not res.is_success:
                raise AssetUnavailable(res.status_code, ticket)
            declared = res.headers.get("content-length")
            if declared is not None and declared.isdigit() and int(declared) > max_bytes:
                raise AssetTooLarge(ticket, max_bytes, int(declared))
            received = 0
            try:
                with open(destination, "wb") as file:
                    async for chunk in res.aiter_bytes():
                        received += len(chunk)
                        if received > max_bytes:
                            raise AssetTooLarge(ticket, max_bytes)
                        file.write(chunk)
            except BaseException:
                with contextlib.suppress(FileNotFoundError):
                    os.remove(destination)
                raise
            return res.headers.get("content-type", "application/octet-stream")

    async def _post(self, path: str, json_body, *, client: httpx.AsyncClient | None, timeout: float = 30) -> httpx.Response:
        headers = {"Authorization": f"Bearer {await self.token(client=client)}"}
        url = f"{self._config.mycelium_url}{path}"
        if client is not None:
            return await client.post(url, json=json_body, headers=headers)
        async with platform_client(timeout=timeout) as c:
            return await c.post(url, json=json_body, headers=headers)
