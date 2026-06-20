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

// ---- Snapshot selector ----
import { subscribe, unsubscribe, sliceByTypeAndTraverse, demoSubscribe, type Config } from "./index.js";

const SEL_CFG: Config = {
  port: 5102,
  myceliumUrl: "http://mycelium.test",
  token: "tok",
  issuer: "VillageOS",
  audience: "VosClients",
};

interface SelCap {
  url: string;
  method: string;
  auth: string | undefined;
  body: string;
}

function stubSelectorFetch(): { calls: SelCap[]; restore: () => void } {
  const calls: SelCap[] = [];
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
            things: [{ id: "t1", name: "Battery-1" }, { id: "t2", name: "Inverter-7" }],
            relationships: [{ id: "r1" }],
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
    const sub = await subscribe(SEL_CFG, sliceByTypeAndTraverse("Battery", "powers"));
    assert.equal(sub.subscriptionId, "s-1");
    assert.equal(sub.watermark, 42);
    assert.equal(sub.snapshot.things.length, 2);
    assert.equal(sub.snapshot.relationships.length, 1);
    const post = s.calls[0];
    assert.ok(post.url.endsWith("/api/subscriptions"));
    assert.equal(post.auth, "Bearer tok");
    assert.ok(post.body.includes('"types"') && post.body.includes("Battery") && post.body.includes("powers"));
  } finally {
    s.restore();
  }
});

test("demoSubscribe summarises the closure and unsubscribes", async () => {
  const s = stubSelectorFetch();
  try {
    const result = await demoSubscribe(SEL_CFG, "Battery", "powers");
    assert.equal(result.things, 2);
    assert.equal(result.relationships, 1);
    assert.deepEqual(result.thingNames, ["Battery-1", "Inverter-7"]);
    // a POST (subscribe) followed by a DELETE (unsubscribe)
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
    await unsubscribe(SEL_CFG, "s-9");
    assert.equal(s.calls[0].method, "DELETE");
    assert.ok(s.calls[0].url.endsWith("/api/subscriptions/s-9"));
  } finally {
    s.restore();
  }
});
