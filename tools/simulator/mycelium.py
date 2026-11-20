#!/usr/bin/env python3
"""A small Mycelium HTTP client for the simulator, written with Python's standard library only.

Every route here is a real Mycelium route. The client needs no packages installed, so the simulator
runs wherever Python does. It knows Things, relationships, Facts, quantity adjustments and
subscriptions, and nothing about any particular model.

Reference: docs/SERVICE_CONTRACT.md.
"""

from __future__ import annotations

import json
import os
import ssl
import urllib.error
import urllib.parse
import urllib.request
import uuid
from urllib.parse import quote, urlencode

# A fixed namespace so stable_id is reproducible across processes and across a resumed run — the
# same action key always hashes to the same UUID.
_ID_NAMESPACE = uuid.UUID("6f9b1e2c-4a7d-5b8e-9c0f-1d2e3a4b5c6d")

# A command line is readable by every process on the host and is kept in the shell's history file, so
# no tool sharing this client takes either credential as an argument.
TOKEN_VARIABLE = "VOS_TOKEN"
API_KEY_VARIABLE = "VOS_API_KEY"


def stable_id(*parts) -> str:
    """A Thing id (a version 5 identifier) derived from a stable action key, so the same ``parts``
    always give the same id. ``POST /api/things`` keeps an ``Id`` the caller supplies, so a run started
    again posts the id that already exists rather than creating a copy. Creating an existing id again is
    refused rather than merged, on purpose: the resume journal skips the actions already done, and an
    action repeated after a crash fails with an error rather than adding a second Thing."""
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
    def __init__(self, url, token=None, api_key=None, model_id=None, timeout=300, insecure=False,
                 environment=None):
        """An in-process caller may hand over a credential directly; a tool run from a shell leaves
        both unset and the environment supplies them. ``environment`` defaults to this process's own
        and is an argument so a test can supply one without touching the process it runs in."""
        environment = os.environ if environment is None else environment
        self.url = url.rstrip("/")
        self._token = token or environment.get(TOKEN_VARIABLE)
        self._api_key = api_key or environment.get(API_KEY_VARIABLE)
        self._model_id = model_id
        self.timeout = timeout
        # The certificate check is skipped only for a local Mycelium with a self-signed certificate, when
        # insecure=True (the --insecure flag) or VOS_TLS_NOVERIFY is set; None keeps the normal check.
        self._ssl = (ssl._create_unverified_context()
                     if insecure or environment.get("VOS_TLS_NOVERIFY") else None)

    def token(self) -> str:
        """Return the bearer token, fetching one with the API key if only that was supplied.

        The token is fetched with ``POST /api/auth/token``, the key in the **``X-API-Key`` header** (not
        the body) and an optional ``?modelId=`` for a host holding several models; it answers
        ``{ "token": <token> }``. An editor or admin token given directly is used as it is."""
        if self._token:
            return self._token
        if not self._api_key:
            raise RuntimeError(
                f"no credentials: set {TOKEN_VARIABLE} to an editor/admin JWT, or "
                f"{API_KEY_VARIABLE} to an API key a token can be minted from")
        path = "/api/auth/token"
        if self._model_id:
            path += "?" + urlencode({"modelId": self._model_id})
        self._token = self._json("POST", path, None,
                                 headers={"X-API-Key": self._api_key}).get("token", "")
        return self._token

    def _json(self, method, path, body=None, headers=None, address=None, _retry=True):
        own = address is None or address.rstrip("/") == self.url
        url = (self.url if own else address.rstrip("/")) + path
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        authed = own and not path.startswith("/api/auth/token")
        if authed:
            req.add_header("Authorization", "Bearer " + self.token())
        for name, value in (headers or {}).items():
            req.add_header(name, value)
        try:
            with urllib.request.urlopen(req, timeout=self.timeout, context=self._ssl) as resp:
                raw = resp.read().decode()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as e:
            # A kept token expires during a long run, so a new one is fetched with the API key and the call
            # tried once more, rather than the run stopping with 401 at its first write after expiry.
            if e.code == 401 and authed and self._api_key and _retry:
                self._token = None
                return self._json(method, path, body, headers, address, _retry=False)
            raise RuntimeError(f"{method} {path} -> {e.code} {e.read().decode()[:200]}")

    def post(self, path, body=None, headers=None, address=None):
        """A post to a route outside the write API, on this host or on ``address`` — a service served
        beside the broker. It takes the same request path as every write, so a post here fetches a new
        token and is tried again when the token expires. The token is sent only to this client's own address:
        another service is never handed the broker's credential, and a caller that needs one there puts
        it in ``headers``."""
        return self._json("POST", path, body, headers=headers, address=address)

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

    def model_time(self):
        """What time the model thinks it is: {now, rate, simulated} (GET /api/time)."""
        return self._json("GET", "/api/time")

    def anchor_model_time(self, now=None, rate=None):
        """Move the model's clock, and set how fast it runs against real time.

        A played timeline is compressed, so the platform and the handlers have to agree on the
        compression or every elapsed measure in the model is the gap between two different clocks.
        Anchoring once at the start is enough: the clock keeps running at `rate` on its own.
        """
        body = {}
        if now is not None:
            body["now"] = now
        if rate is not None:
            body["rate"] = rate
        return self._json("POST", "/api/time", body)

    def thing_id_by_name(self, name):
        """The id the model holds this name under, or None if it holds no such Thing. A name the
        model holds more than once is an error rather than a pick — answering with one of them would
        wire something to whichever happened to be first."""
        try:
            answer = self._json("GET", "/api/things?name=" + urllib.parse.quote(name, safe=""))
        except RuntimeError as error:
            if " -> 404 " in str(error):
                return None
            raise
        return answer.get("Id") or answer.get("id")

    def load_model(self, document):
        """Load a {Things, Relationships} document in one call (POST /api/model)."""
        return self._json("POST", "/api/model", document)

    def apply_fragment(self, things, relationships, name="simulator fragment"):
        """Apply a fragment ``{Name, Things, Relationships}`` in one call to ``POST /api/model/fragment``.

        A Thing may not hold a property of its own under a name it inherits, so the route creates each
        Thing bare, adds its ``is`` relationships, and then stores any inherited value as an override.
        It adds what is missing and updates what is there, so posting the same fragment again neither
        duplicates anything nor fails. ``things`` are dicts in ThingDto shape (``Id``, ``Name``, typed
        ``Properties``); ``relationships`` are dicts in RelDto shape (``Name``, ``Subject``,
        ``Predicate``, ``Target``, optional ``Id``)."""
        return self._json("POST", "/api/model/fragment",
                          {"Name": name, "Things": things, "Relationships": relationships})

    @staticmethod
    def fragment_thing(thing_id, name, properties=None):
        """Build a ThingDto: id, name, and plain scalars wrapped in typed envelopes."""
        return {"Id": thing_id, "Name": name, "Properties": typed_properties(properties)}

    @staticmethod
    def fragment_rel(subject_id, predicate_id, target_id, name=None):
        """Build a RelDto for a fragment's Relationships list."""
        return {"Name": name or "rel", "Subject": subject_id,
                "Predicate": predicate_id, "Target": target_id}

    def subscribe(self, selector: dict):
        return self._json("POST", "/api/subscriptions", selector)

    def unsubscribe(self, subscription_id):
        try:
            self._json("DELETE", f"/api/subscriptions/{subscription_id}")
        except Exception:
            pass

    def stream_token(self) -> str:
        """Mint the credential a stream address may carry (``POST /api/auth/stream-token``) — the only
        token the platform admits there, because an address is recorded where a header is not. A client
        that can set a header wants ``follow()`` instead, which sends the bearer and mints nothing."""
        return self._json("POST", "/api/auth/stream-token", None).get("token", "")

    def stream_url(self, subscription_id, last=0):
        """An event-stream address a browser can open, carrying its credential in the query, because a
        browser's EventSource cannot set the Authorization header: a stream token goes in
        ``?access_token=`` and the point to resume from in ``&lastEventId=``, as Trellis's useSse hook builds it."""
        query = {"access_token": self.stream_token()}
        if last:
            query["lastEventId"] = last
        return f"{self.url}/api/subscriptions/{subscription_id}/stream?{urlencode(query)}"

    def follow(self, subscription_id, last=0, token_in_query=False):
        """Yield (kind, data, event_id) for each change event the stream sends, resuming from ``last``."""
        if token_in_query:
            req = urllib.request.Request(self.stream_url(subscription_id, last))
        else:
            req = urllib.request.Request(f"{self.url}/api/subscriptions/{subscription_id}/stream")
            req.add_header("Authorization", "Bearer " + self.token())
        req.add_header("Accept", "text/event-stream")
        req.add_header("Last-Event-ID", str(last))
        with urllib.request.urlopen(req, timeout=None, context=self._ssl) as resp:
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
