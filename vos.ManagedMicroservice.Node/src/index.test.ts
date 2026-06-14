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
