#!/usr/bin/env python3
"""A minimal, standard-library Mycelium HTTP client for the simulator.

Every route here is a real Mycelium endpoint (the VillageOS gateway). The client is deliberately
small and dependency-free so the simulator drops into any environment with a stock Python. It is
domain-agnostic — it knows Things, Relationships, Facts, quantity adjustments, and subscriptions,
nothing about any particular model.

Reference: docs/MICROSERVICE_CONTRACT.md.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
import uuid
from urllib.parse import quote, urlencode

# A fixed namespace so stable_id is reproducible across processes and across a resumed run — the
# same action key always hashes to the same UUID.
_ID_NAMESPACE = uuid.UUID("6f9b1e2c-4a7d-5b8e-9c0f-1d2e3a4b5c6d")


def stable_id(*parts) -> str:
    """A deterministic Thing id (UUIDv5) derived from a stable action key. The same ``parts`` always
    yield the same id — how a resumable driver stays idempotent: ``POST /api/things`` honours a
    client-supplied ``Id`` (``dto.Id ?? Guid.NewGuid()``), so keying a created Thing to
    ``stable_id(...)`` means a re-run POSTs the id that already exists rather than minting a duplicate.
    Re-creating an existing id is *rejected* (not merged), which is intended: the resume checkpoint
    skips committed actions, and the deterministic id makes any straggler fail loud rather than fork."""
    return str(uuid.uuid5(_ID_NAMESPACE, "/".join(str(part) for part in parts)))


def typed(value, type_info=None):
    """Wrap a value in the platform's typed property envelope. Decimals MUST be typed explicitly or
    the platform truncates them to whole numbers."""
    if type_info is None:
        if isinstance(value, bool):
            type_info = "vos.Boolean"
        elif isinstance(value, float):
            type_info = "vos.Decimal"
        elif isinstance(value, int):
            type_info = "vos.LongInteger"
        else:
            type_info = "vos.String"
    return {"typeInfo": type_info, "value": value}


def typed_properties(properties):
    """Wrap each plain scalar in ``properties`` with ``typed``; pass through any value that is already
    an object (an existing envelope) or a list."""
    return {name: (value if isinstance(value, (dict, list)) else typed(value))
            for name, value in (properties or {}).items()}


class MyceliumClient:
    def __init__(self, url, token=None, api_key=None, model_id=None, timeout=30):
        self.url = url.rstrip("/")
        self._token = token
        self._api_key = api_key
        self._model_id = model_id
        self.timeout = timeout

    # -- auth -------------------------------------------------------------
    def token(self) -> str:
        """Return the bearer JWT, minting one from the API key if only that was supplied.

        The mint is ``POST /api/auth/token`` with the key in the **``X-API-Key`` header** (not a body)
        and an optional ``?modelId=`` for a multi-model host; it returns ``{ "token": <jwt> }``. Pass a
        ready editor/admin JWT via ``token=`` to skip this entirely."""
        if self._token:
            return self._token
        if not self._api_key:
            raise RuntimeError("no credentials: pass a token=<jwt> or api_key=<key>")
        path = "/api/auth/token"
        if self._model_id:
            path += "?" + urlencode({"modelId": self._model_id})
        self._token = self._json("POST", path, None,
                                 headers={"X-API-Key": self._api_key}).get("token", "")
        return self._token

    def _json(self, method, path, body=None, headers=None):
        url = self.url + path
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if not path.startswith("/api/auth/token"):
            req.add_header("Authorization", "Bearer " + self.token())
        for name, value in (headers or {}).items():
            req.add_header(name, value)
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                raw = resp.read().decode()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as e:
            raise RuntimeError(f"{method} {path} -> {e.code} {e.read().decode()[:200]}")

    # -- writes -----------------------------------------------------------
    def create_thing(self, name, properties=None, thing_id=None):
        body = {"Name": name, "Properties": properties or {}}
        if thing_id is not None:
            body["Id"] = thing_id
        return self._json("POST", "/api/things", body)

    def create_typed_thing(self, name, properties=None, thing_id=None):
        return self.create_thing(name, typed_properties(properties), thing_id=thing_id)

    def create_relationship(self, subject_id, predicate_id, target_id):
        return self._json("POST", "/api/relationships",
                          {"SubjectId": subject_id, "PredicateId": predicate_id, "TargetId": target_id})

    def delete_thing(self, thing_id):
        return self._json("DELETE", f"/api/things/{thing_id}")

    def set_fact(self, thing_id, prop, value):
        p = quote(prop, safe="")
        return self._json("POST", f"/api/things/{thing_id}/properties/{p}/facts", {"value": value})

    def set_observation(self, thing_id, prop, value, observed_at=None):
        p = quote(prop, safe="")
        body = {"value": value}
        if observed_at is not None:
            body["observedAt"] = observed_at
        return self._json("POST", f"/api/things/{thing_id}/properties/{p}/observations", body)

    def increment(self, thing_id, prop, amount):
        p = quote(prop, safe="")
        return self._json("POST", f"/api/things/{thing_id}/properties/{p}/increments", {"amount": amount})

    def decrement(self, thing_id, prop, amount):
        p = quote(prop, safe="")
        return self._json("POST", f"/api/things/{thing_id}/properties/{p}/decrements", {"amount": amount})

    def load_model(self, document):
        """Bulk-load a {Things, Relationships} document in one shot (POST /api/model)."""
        return self._json("POST", "/api/model", document)

    def apply_fragment(self, things, relationships, name="simulator fragment"):
        """Upsert a partial-model fragment ``{Name, Things, Relationships}`` in one shot via
        ``POST /api/model/fragment``.

        This is how the simulator honours lazy inheritance (I1: a Thing may not *own* a property name
        it inherits) **server-side**: the endpoint creates each Thing bare, establishes its ``is``
        edges, and materializes any inherited value as an override itself — so the client no longer
        choreographs bare-create-then-override. It is an upsert and idempotent: re-posting the same
        fragment neither duplicates Things/Relationships nor errors. ``things`` are dicts already in
        ThingDto shape (``Id``, ``Name``, typed ``Properties``); ``relationships`` are dicts in RelDto
        shape (``Name``, ``Subject``, ``Predicate``, ``Target``, optional ``Id``)."""
        return self._json("POST", "/api/model/fragment",
                          {"Name": name, "Things": things, "Relationships": relationships})

    # -- fragment builders ------------------------------------------------
    @staticmethod
    def fragment_thing(thing_id, name, properties=None):
        """Build a ThingDto: id, name, and plain scalars wrapped in typed envelopes."""
        return {"Id": thing_id, "Name": name, "Properties": typed_properties(properties)}

    @staticmethod
    def fragment_rel(subject_id, predicate_id, target_id, name=None):
        """Build a RelDto for a fragment's Relationships list."""
        return {"Name": name or "rel", "Subject": subject_id,
                "Predicate": predicate_id, "Target": target_id}

    # -- subscriptions ----------------------------------------------------
    def subscribe(self, selector: dict):
        return self._json("POST", "/api/subscriptions", selector)

    def unsubscribe(self, subscription_id):
        try:
            self._json("DELETE", f"/api/subscriptions/{subscription_id}")
        except Exception:
            pass

    def stream_url(self, subscription_id, last=0):
        """A browser-usable SSE URL that carries auth in the query string, since EventSource cannot
        set the Authorization header — the JWT rides as ``?access_token=`` and resume as
        ``&lastEventId=``. This is exactly what Trellis's useSse hook builds."""
        query = {"access_token": self.token()}
        if last:
            query["lastEventId"] = last
        return f"{self.url}/api/subscriptions/{subscription_id}/stream?{urlencode(query)}"

    def follow(self, subscription_id, last=0, token_in_query=False):
        """Yield (kind, data, event_id) SSE change events; resume via Last-Event-ID."""
        if token_in_query:
            req = urllib.request.Request(self.stream_url(subscription_id, last))
        else:
            req = urllib.request.Request(f"{self.url}/api/subscriptions/{subscription_id}/stream")
            req.add_header("Authorization", "Bearer " + self.token())
        req.add_header("Accept", "text/event-stream")
        req.add_header("Last-Event-ID", str(last))
        with urllib.request.urlopen(req, timeout=None) as resp:
            event_id = kind = data = None
            for raw in resp:
                line = raw.decode().rstrip("\n")
                if line.startswith("id:"):
                    event_id = line[3:].strip()
                elif line.startswith("event:"):
                    kind = line[6:].strip()
                elif line.startswith("data:"):
                    data = line[5:].strip()
                elif line == "" and data:
                    yield kind, json.loads(data), event_id
                    event_id = kind = data = None
