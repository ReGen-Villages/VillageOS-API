// Tests for the pure helpers (arg parsing + HS256 JWT validation) using Node's
// built-in test runner — no extra dependencies. Run via `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHmac } from "node:crypto";
import { parseArgs, verifyJwt } from "./index.js";

const KEY = Buffer.from("vos-test-signing-key-0123456789ab", "utf8");

function b64url(obj: unknown): string {
  return Buffer.from(JSON.stringify(obj)).toString("base64url");
}

function makeToken(key: Buffer, overrides: Record<string, unknown> = {}): string {
  const now = Math.floor(Date.now() / 1000);
  const claims = {
    iss: "VillageOS",
    aud: "VosClients",
    sub: "mycelium",
    "vos:token_type": "mycelium_request",
    iat: now,
    nbf: now,
    exp: now + 60,
    ...overrides,
  };
  const head = b64url({ alg: "HS256", typ: "JWT" });
  const pay = b64url(claims);
  const sig = createHmac("sha256", key).update(`${head}.${pay}`).digest("base64url");
  return `${head}.${pay}.${sig}`;
}

test("parseArgs: valid required flags + defaults", () => {
  const cfg = parseArgs(["--port=5102", "--myceliumUrl=https://localhost:7243/"]);
  assert.ok(cfg);
  assert.equal(cfg.port, 5102);
  assert.equal(cfg.myceliumUrl, "https://localhost:7243");
  assert.equal(cfg.issuer, "VillageOS");
  assert.equal(cfg.audience, "VosClients");
});

test("parseArgs: missing required flags returns null", () => {
  assert.equal(parseArgs(["--port=5102"]), null);
  assert.equal(parseArgs(["--myceliumUrl=x"]), null);
  assert.equal(parseArgs(["--port=0", "--myceliumUrl=x"]), null);
});

test("verifyJwt: accepts a valid token", () => {
  assert.equal(verifyJwt(makeToken(KEY), KEY, "VillageOS", "VosClients"), true);
});

test("verifyJwt: rejects tampered signature", () => {
  assert.equal(verifyJwt(makeToken(KEY) + "x", KEY, "VillageOS", "VosClients"), false);
});

test("verifyJwt: rejects wrong key", () => {
  const other = Buffer.from("a-totally-different-signing-key!!", "utf8");
  assert.equal(verifyJwt(makeToken(KEY), other, "VillageOS", "VosClients"), false);
});

test("verifyJwt: rejects expired token", () => {
  const tok = makeToken(KEY, { exp: Math.floor(Date.now() / 1000) - 120 });
  assert.equal(verifyJwt(tok, KEY, "VillageOS", "VosClients"), false);
});

test("verifyJwt: rejects wrong issuer / audience", () => {
  assert.equal(verifyJwt(makeToken(KEY, { iss: "Attacker" }), KEY, "VillageOS", "VosClients"), false);
  assert.equal(verifyJwt(makeToken(KEY, { aud: "Nope" }), KEY, "VillageOS", "VosClients"), false);
});

test("verifyJwt: rejects malformed token", () => {
  assert.equal(verifyJwt("not-a-jwt", KEY, "VillageOS", "VosClients"), false);
});

// ---- Write kinds (Fact / Observation / Sediment) ----
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
  audience: "VosClients",
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
    const r = await depositSediment(CFG, [{ thingId: "t1", property: "flow", value: 1.0, observedAt: "2026-06-19T00:00:00Z" }]);
    assert.equal(r.batchId, "b-1");
    assert.equal(r.samples, 10);
    assert.ok(s.calls[0].url.endsWith("/api/sediment"));
    assert.ok(s.calls[0].body.includes("observedAt"));
  } finally {
    s.restore();
  }
});

test("depositSediment: empty batch throws", async () => {
  await assert.rejects(depositSediment(CFG, []), /at least one reading/);
});
