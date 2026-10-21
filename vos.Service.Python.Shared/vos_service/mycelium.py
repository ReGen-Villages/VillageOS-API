from __future__ import annotations

from urllib.parse import quote

import httpx

from .config import Config

# Every method takes an optional `client`, so a test can inject an httpx.AsyncClient with a MockTransport.
# Write kinds — Facts, Observations, Sediment: docs/SERVICE_CONTRACT.md § "Writing data back".
# Snapshot selector — subscribe to a slice: docs/SERVICE_CONTRACT.md § "Selecting a slice".


class MyceliumClient:
    """A service's calls to the Mycelium gateway. `config` is read on each call."""

    def __init__(self, config: Config):
        self._config = config

    async def token(self, *, client: httpx.AsyncClient | None = None) -> str:
        if self._config.token:
            return self._config.token
        url = f"{self._config.mycelium_url}/api/auth/token"
        if client is not None:
            res = await client.post(url)
        else:
            async with httpx.AsyncClient(timeout=5, verify=False) as c:
                res = await c.post(url)
        res.raise_for_status()
        return res.json()["token"]

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
        """Bulk-load historical readings (dicts of {thingId, property, value, observedAt}) to sealed Sapwood (202)."""
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
            async with httpx.AsyncClient(timeout=10, verify=False) as c:
                await c.delete(url, headers=headers)

    async def _post(self, path: str, json_body, *, client: httpx.AsyncClient | None, timeout: float = 30) -> httpx.Response:
        headers = {"Authorization": f"Bearer {await self.token(client=client)}"}
        url = f"{self._config.mycelium_url}{path}"
        if client is not None:
            return await client.post(url, json=json_body, headers=headers)
        async with httpx.AsyncClient(timeout=timeout, verify=False) as c:
            return await c.post(url, json=json_body, headers=headers)
