// Tests for the pure helpers (arg parsing + inbound JWT validation) using Node's
// built-in test runner — no extra dependencies. Run via `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHmac, generateKeyPairSync, sign, type KeyObject } from "node:crypto";
import { parseArgs, verificationKeyFrom, verifyJwt } from "./index.js";

const THIS_HANDLER = "node-echo-handler";

// Stands in for the pair Mycelium generates.
const MYCELIUM = generateKeyPairSync("ec", { namedCurve: "prime256v1" });

/** The public half, encoded the way Mycelium hands it to a daemon. */
function verificationKey(): string {
  return MYCELIUM.publicKey.export({ format: "der", type: "spki" }).toString("base64");
}

const PUBLIC_KEY: KeyObject = verificationKeyFrom(verificationKey());

function b64url(obj: unknown): string {
  return Buffer.from(JSON.stringify(obj)).toString("base64url");
}

function claimsWith(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  const now = Math.floor(Date.now() / 1000);
  return {
    iss: "VillageOS",
    aud: THIS_HANDLER,
    sub: "mycelium",
    "vos:token_type": "mycelium_request",
    iat: now,
    nbf: now,
    exp: now + 60,
    ...overrides,
  };
}

/** Signs the way Mycelium does. */
function makeToken(overrides: Record<string, unknown> = {}): string {
  const signing = `${b64url({ alg: "ES256", typ: "JWT" })}.${b64url(claimsWith(overrides))}`;
  const signature = sign("sha256", Buffer.from(signing), {
    key: MYCELIUM.privateKey,
    dsaEncoding: "ieee-p1363",
  });
  return `${signing}.${signature.toString("base64url")}`;
}

/** What someone who reads the verification key off a daemon can produce. */
function forgedFromTheVerificationKey(): string {
  const signing = `${b64url({ alg: "HS256", typ: "JWT" })}.${b64url(claimsWith())}`;
  const sig = createHmac("sha256", Buffer.from(verificationKey(), "base64"))
    .update(signing)
    .digest("base64url");
  return `${signing}.${sig}`;
}

test("parseArgs: valid required flags, and nothing defaulted", () => {
  const cfg = parseArgs(["--port=5102", "--myceliumUrl=https://localhost:7243/"], {});
  assert.ok(cfg);
  assert.equal(cfg.port, 5102);
  assert.equal(cfg.myceliumUrl, "https://localhost:7243");
  assert.equal(cfg.issuer, "");
  assert.equal(cfg.audience, "");
});

test("parseArgs: missing required flags returns null", () => {
  assert.equal(parseArgs(["--port=5102"], {}), null);
  assert.equal(parseArgs(["--myceliumUrl=x"], {}), null);
  assert.equal(parseArgs(["--port=0", "--myceliumUrl=x"], {}), null);
});

// Each handler is addressed by its own name, so there is no shared default left that could be right.
test("parseArgs: a verification key with no issuer or recipient name is refused", () => {
  const environment = { VerificationKey: "ZW52aXJvbm1lbnQta2V5" };
  assert.equal(
    parseArgs(["--port=5102", "--myceliumUrl=https://x", "--issuer=VillageOS"], environment), null);
  assert.equal(
    parseArgs(["--port=5102", "--myceliumUrl=https://x", `--audience=${THIS_HANDLER}`], environment), null);
});

test("parseArgs: credentials come from the environment", () => {
  const cfg = parseArgs(
    ["--port=5102", "--myceliumUrl=https://localhost:7243", "--issuer=VillageOS", `--audience=${THIS_HANDLER}`],
    { Token: "environment-token", VerificationKey: "ZW52aXJvbm1lbnQta2V5" },
  );
  assert.ok(cfg);
  assert.equal(cfg.token, "environment-token");
  assert.equal(cfg.verificationKey, "ZW52aXJvbm1lbnQta2V5");
});

test("parseArgs: a credential given as a flag is ignored", () => {
  const cfg = parseArgs(
    ["--port=5102", "--myceliumUrl=https://localhost:7243", "--token=flag-token", "--verificationKey=flag-key"],
    {},
  );
  assert.ok(cfg);
  assert.equal(cfg.token, undefined);
  assert.equal(cfg.verificationKey, undefined);
});

test("verifyJwt: accepts a valid token", () => {
  assert.equal(verifyJwt(makeToken(), PUBLIC_KEY, "VillageOS", THIS_HANDLER), true);
});

test("verifyJwt: rejects tampered signature", () => {
  assert.equal(verifyJwt(makeToken() + "x", PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
});

test("verifyJwt: rejects a token signed by another Mycelium", () => {
  const stranger = generateKeyPairSync("ec", { namedCurve: "prime256v1" });
  const other = verificationKeyFrom(
    stranger.publicKey.export({ format: "der", type: "spki" }).toString("base64"));
  assert.equal(verifyJwt(makeToken(), other, "VillageOS", THIS_HANDLER), false);
});

// Every handler holds the verification key. A checker that honoured the algorithm the token names
// would let any of them sign one.
test("verifyJwt: rejects the verification key used as a shared secret", () => {
  assert.equal(
    verifyJwt(forgedFromTheVerificationKey(), PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
});

test("verifyJwt: rejects a token carrying no signature", () => {
  const unsigned = `${b64url({ alg: "none", typ: "JWT" })}.${b64url(claimsWith())}.`;
  assert.equal(verifyJwt(unsigned, PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
});

test("verifyJwt: rejects expired token", () => {
  const tok = makeToken({ exp: Math.floor(Date.now() / 1000) - 120 });
  assert.equal(verifyJwt(tok, PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
});

test("verifyJwt: rejects a wrong issuer, another service's name, and a person's browser token", () => {
  assert.equal(verifyJwt(makeToken({ iss: "Attacker" }), PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
  assert.equal(verifyJwt(makeToken({ aud: "spring-handler" }), PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
  assert.equal(verifyJwt(makeToken({ aud: "VosClients" }), PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
});

test("verifyJwt: rejects malformed token", () => {
  assert.equal(verifyJwt("not-a-jwt", PUBLIC_KEY, "VillageOS", THIS_HANDLER), false);
});

import {
  setFact,
  recordObservation,
  recordObservations,
  depositSediment,
  type Config,
} from "./index.js";

const CFG: Config = {
  port: 5102,
  myceliumUrl: "http://mycelium.test",
  token: "tok",
  issuer: "VillageOS",
  audience: THIS_HANDLER,
};

interface Captured {
  url: string;
  method: string;
  auth: string | undefined;
  body: string;
}

function stubFetch(reply: (url: string) => { status: number; json?: unknown }): {
  calls: Captured[];
  restore: () => void;
} {
  const calls: Captured[] = [];
  const original = globalThis.fetch;
  globalThis.fetch = (async (input: unknown, init?: RequestInit): Promise<Response> => {
    const url = String(input);
    const headers = (init?.headers ?? {}) as Record<string, string>;
    calls.push({ url, method: init?.method ?? "GET", auth: headers["Authorization"], body: String(init?.body ?? "") });
    const r = reply(url);
    return new Response(r.json !== undefined ? JSON.stringify(r.json) : null, {
      status: r.status,
      headers: { "Content-Type": "application/json" },
    });
  }) as typeof fetch;
  return { calls, restore: () => void (globalThis.fetch = original) };
}

test("setFact: POSTs value to /facts and returns sequence", async () => {
  const s = stubFetch((url) => (url.endsWith("/facts") ? { status: 201, json: { sequenceNumber: 42 } } : { status: 404 }));
  try {
    const seq = await setFact(CFG, "t1", "status", "active");
    assert.equal(seq, 42);
    assert.equal(s.calls.length, 1);
    assert.ok(s.calls[0].url.endsWith("/api/things/t1/properties/status/facts"));
    assert.equal(s.calls[0].auth, "Bearer tok");
    assert.equal(JSON.parse(s.calls[0].body).value, "active");
  } finally {
    s.restore();
  }
});

test("setFact: 405 (ObservationOnly) throws", async () => {
  const s = stubFetch(() => ({ status: 405 }));
  try {
    await assert.rejects(setFact(CFG, "t1", "temperature", 1), /405/);
  } finally {
    s.restore();
  }
});

test("recordObservation: posts value + observedAt (202)", async () => {
  const s = stubFetch(() => ({ status: 202 }));
  try {
    await recordObservation(CFG, "t1", "temperature", 21.5, "2026-06-20T14:00:00Z");
    assert.ok(s.calls[0].url.endsWith("/api/things/t1/properties/temperature/observations"));
    const body = JSON.parse(s.calls[0].body);
    assert.equal(body.value, 21.5);
    assert.equal(body.observedAt, "2026-06-20T14:00:00Z");
  } finally {
    s.restore();
  }
});

test("recordObservation: omits observedAt when not given", async () => {
  const s = stubFetch(() => ({ status: 202 }));
  try {
    await recordObservation(CFG, "t1", "temperature", 21.5);
    assert.equal(JSON.parse(s.calls[0].body).observedAt, undefined);
  } finally {
    s.restore();
  }
});

test("recordObservations: batch posts array and returns accepted", async () => {
  const s = stubFetch(() => ({ status: 202, json: { accepted: 2 } }));
  try {
    const n = await recordObservations(CFG, "t1", [
      { property: "temperature", value: 21.7 },
      { property: "flow", value: 3.1, observedAt: "2026-06-20T14:00:00Z" },
    ]);
    assert.equal(n, 2);
    assert.ok(s.calls[0].url.endsWith("/api/things/t1/observations"));
    assert.ok(Array.isArray(JSON.parse(s.calls[0].body)));
  } finally {
    s.restore();
  }
});

test("recordObservations: empty batch makes no call", async () => {
  const s = stubFetch(() => ({ status: 500 }));
  try {
    assert.equal(await recordObservations(CFG, "t1", []), 0);
    assert.equal(s.calls.length, 0);
  } finally {
    s.restore();
  }
});

test("depositSediment: posts readings and returns summary", async () => {
  const s = stubFetch(() => ({ status: 202, json: { batchId: "b-1", series: 1, buckets: 3, samples: 10 } }));
  try {
    const r = await depositSediment(CFG, [{ objectId: "t1", property: "flow", value: 1.0, observedAt: "2026-06-19T00:00:00Z" }]);
    assert.equal(r.batchId, "b-1");
    assert.equal(r.samples, 10);
    assert.ok(s.calls[0].url.endsWith("/api/sediment"));
    assert.ok(s.calls[0].body.includes("observedAt"));
    assert.equal(JSON.parse(s.calls[0].body)[0].objectId, "t1", "the sediment route reads the Thing from objectId");
  } finally {
    s.restore();
  }
});

test("depositSediment: empty batch throws", async () => {
  await assert.rejects(depositSediment(CFG, []), /at least one reading/);
});

import { subscribe, unsubscribe, sliceByTypeAndTraverse, demoSubscribe } from "./index.js";

function stubSelectorFetch(): { calls: Captured[]; restore: () => void } {
  const calls: Captured[] = [];
  const original = globalThis.fetch;
  globalThis.fetch = (async (input: unknown, init?: RequestInit): Promise<Response> => {
    const url = String(input);
    const headers = (init?.headers ?? {}) as Record<string, string>;
    calls.push({ url, method: init?.method ?? "GET", auth: headers["Authorization"], body: String(init?.body ?? "") });
    if (url.endsWith("/api/subscriptions") && init?.method === "POST") {
      return new Response(
        JSON.stringify({
          subscriptionId: "s-1",
          watermark: 42,
          snapshot: {
            things: [{ Id: "t1", Name: "Battery-1" }, { Id: "t2", Name: "Inverter-7" }],
            relationships: [{ Id: "r1" }],
          },
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(null, { status: 200 }); // DELETE unsubscribe
  }) as typeof fetch;
  return { calls, restore: () => void (globalThis.fetch = original) };
}

test("sliceByTypeAndTraverse builds a type+traverse selector", () => {
  const sel = sliceByTypeAndTraverse("Battery", "powers");
  assert.deepEqual(sel.types, ["Battery"]);
  assert.equal(sel.traverse?.[0].predicate, "powers");
  assert.equal(sel.traverse?.[0].direction, "outgoing");
});

test("subscribe posts the selector and returns the closure", async () => {
  const s = stubSelectorFetch();
  try {
    const sub = await subscribe(CFG, sliceByTypeAndTraverse("Battery", "powers"));
    assert.equal(sub.subscriptionId, "s-1");
    assert.equal(sub.snapshot.things.length, 2);
    assert.equal(sub.snapshot.relationships.length, 1);
    assert.ok(s.calls[0].url.endsWith("/api/subscriptions"));
    assert.equal(s.calls[0].auth, "Bearer tok");
    assert.ok(s.calls[0].body.includes('"types"') && s.calls[0].body.includes("Battery") && s.calls[0].body.includes("powers"));
  } finally {
    s.restore();
  }
});

test("demoSubscribe summarises the closure and unsubscribes", async () => {
  const s = stubSelectorFetch();
  try {
    const result = await demoSubscribe(CFG, "Battery", "powers");
    assert.equal(result.things, 2);
    assert.equal(result.relationships, 1);
    assert.deepEqual(result.thingNames, ["Battery-1", "Inverter-7"]);
    assert.equal(s.calls[0].method, "POST");
    assert.equal(s.calls[1].method, "DELETE");
    assert.ok(s.calls[1].url.includes("/api/subscriptions/s-1"));
  } finally {
    s.restore();
  }
});

test("unsubscribe issues a DELETE", async () => {
  const s = stubSelectorFetch();
  try {
    await unsubscribe(CFG, "s-9");
    assert.equal(s.calls[0].method, "DELETE");
    assert.ok(s.calls[0].url.endsWith("/api/subscriptions/s-9"));
  } finally {
    s.restore();
  }
});

import { shutdown } from "./index.js";
import { createServer } from "node:http";

// The deregistration route is admin-only; the broker's liveness monitor removes a registration whose service stops answering.
test("shutdown does not ask the broker to withdraw", async () => {
  const s = stubFetch(() => ({ status: 200 }));
  const server = createServer();
  await new Promise<void>((resolve) => server.listen(0, "localhost", resolve));
  try {
    await shutdown(server);
    assert.deepEqual(s.calls, [], "a service cannot deregister itself, so shutdown must not try");
  } finally {
    s.restore();
  }
});

import { mock } from "node:test";
import { getToken, registration, reasonACallFailed, USAGE } from "./index.js";

const A_MOMENT = Date.parse("2026-10-02T12:00:00Z");
const FIVE_MINUTES = 5 * 60_000;

function tokenWithClaims(claims: Record<string, unknown>): string {
  return `${b64url({ alg: "ES256" })}.${b64url(claims)}.not-a-signature`;
}

function tokenExpiringAt(milliseconds: number): string {
  return tokenWithClaims({ sub: "a-service", exp: Math.floor(milliseconds / 1000) });
}

function withAKey(overrides: Partial<Config> = {}): Config {
  return { port: 5102, myceliumUrl: "http://mycelium.test", apiKey: "vos_ak_example", issuer: "", audience: "", ...overrides };
}

/** Stands in for the route that exchanges a key, and records the key each call presented. */
function stubKeyExchange(answer: () => { status: number; token?: string }): { keysPresented: (string | undefined)[]; restore: () => void } {
  const keysPresented: (string | undefined)[] = [];
  const original = globalThis.fetch;
  globalThis.fetch = (async (input: unknown, init?: RequestInit): Promise<Response> => {
    assert.ok(String(input).endsWith("/api/auth/token"), `unexpected call to ${String(input)}`);
    keysPresented.push(((init?.headers ?? {}) as Record<string, string>)["X-API-Key"]);
    const { status, token } = answer();
    return new Response(JSON.stringify({ token }), { status });
  }) as typeof fetch;
  return { keysPresented, restore: () => void (globalThis.fetch = original) };
}

test("parseArgs: an API key is read from the environment and never from a flag", () => {
  const flags = ["--port=5102", "--myceliumUrl=https://localhost:7243", "--apiKey=flag-key"];
  assert.equal(parseArgs(flags, { ApiKey: "environment-key" })?.apiKey, "environment-key");
  assert.equal(parseArgs(flags, {})?.apiKey, undefined);
});

test("a key is exchanged in the key header, and the token is held", async () => {
  mock.timers.enable({ apis: ["Date"], now: A_MOMENT });
  const minted = tokenExpiringAt(A_MOMENT + FIVE_MINUTES);
  const exchange = stubKeyExchange(() => ({ status: 200, token: minted }));
  try {
    const cfg = withAKey();
    for (let call = 0; call < 3; call++) assert.equal(await getToken(cfg), minted);
    assert.deepEqual(exchange.keysPresented, ["vos_ak_example"]);
  } finally {
    exchange.restore();
    mock.timers.reset();
  }
});

test("a held token is exchanged again shortly before it runs out", async () => {
  mock.timers.enable({ apis: ["Date"], now: A_MOMENT });
  const exchange = stubKeyExchange(() => ({ status: 200, token: tokenExpiringAt(Date.now() + FIVE_MINUTES) }));
  try {
    const cfg = withAKey();
    for (let call = 0; call < 3; call++) {
      await getToken(cfg);
      mock.timers.setTime(Date.now() + FIVE_MINUTES - 20_000);
    }
    assert.equal(
      exchange.keysPresented.length, 3,
      "twenty seconds from running out is inside the half minute a token is replaced in");
  } finally {
    exchange.restore();
    mock.timers.reset();
  }
});

test("a token that states no expiry is not held", async () => {
  const exchange = stubKeyExchange(() => ({ status: 200, token: tokenWithClaims({ sub: "a-service" }) }));
  try {
    await assert.rejects(getToken(withAKey()), /no expiry/);
  } finally {
    exchange.restore();
  }
});

test("a refused key says what the platform answered", async () => {
  const exchange = stubKeyExchange(() => ({ status: 401 }));
  try {
    await assert.rejects(getToken(withAKey()), /refused to exchange the API key \(401\)/);
  } finally {
    exchange.restore();
  }
});

test("a key is presented before a token given beside it", async () => {
  mock.timers.enable({ apis: ["Date"], now: A_MOMENT });
  const minted = tokenExpiringAt(A_MOMENT + FIVE_MINUTES);
  const exchange = stubKeyExchange(() => ({ status: 200, token: minted }));
  try {
    assert.equal(await getToken(withAKey({ token: "a-launch-token" })), minted);
  } finally {
    exchange.restore();
    mock.timers.reset();
  }
});

test("with neither a key nor a token no call is made and the service says so", async () => {
  const s = stubFetch(() => ({ status: 401 }));
  try {
    const said = await registration(withAKey({ apiKey: undefined }));
    assert.deepEqual(s.calls, [], "a call carrying no credential is always refused");
    assert.match(said, /ApiKey/);
    assert.match(said, /Token/);
  } finally {
    s.restore();
  }
});

test("the usage text names the key among the credentials", () => {
  assert.match(USAGE, /ApiKey/);
});

// Node's built-in fetch reports a refused certificate as "fetch failed" and names the reason only on the cause.
test("a certificate Node does not trust is reported with the setting that makes Node trust it", () => {
  const refused = new TypeError("fetch failed", { cause: Object.assign(new Error("self-signed certificate"), { code: "DEPTH_ZERO_SELF_SIGNED_CERT" }) });
  assert.match(reasonACallFailed(refused), /NODE_EXTRA_CA_CERTS/);
  assert.equal(reasonACallFailed(new Error("register returned 403")), "register returned 403");
});

test("a call that fails on the certificate is the reason registration gives", async () => {
  const original = globalThis.fetch;
  globalThis.fetch = (async () => {
    throw new TypeError("fetch failed", { cause: Object.assign(new Error("self-signed certificate"), { code: "DEPTH_ZERO_SELF_SIGNED_CERT" }) });
  }) as typeof fetch;
  try {
    assert.match(await registration(CFG), /registration failed.*NODE_EXTRA_CA_CERTS/);
  } finally {
    globalThis.fetch = original;
  }
});
