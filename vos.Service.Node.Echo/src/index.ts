// VillageOS managed-microservice example — Node.js + TypeScript (built-ins only).
//
// A managed microservice is a handler that Mycelium (the VillageOS gateway)
// launches as a daemon and calls when a relationship with the service's
// predicate is created. The whole contract is HTTP + a single HS256 JWT.
//
// `is` is NOT an external predicate — Mycelium handles `is` inheritance
// in-process and never dispatches it. Register for a custom predicate instead.

import { createServer, IncomingMessage, ServerResponse } from "node:http";
import { createHmac, randomUUID, timingSafeEqual } from "node:crypto";
import { pathToFileURL } from "node:url";

const SERVICE_NAME = "Node";

export interface Config {
  port: number;
  myceliumUrl: string;
  token?: string;
  signingKey?: string;
  issuer: string;
  audience: string;
}

const USAGE = `Usage: node index.js --port=<port> --myceliumUrl=<url> [--issuer=<iss>] [--audience=<aud>]
  --port        Port to listen on (1-65535)
  --myceliumUrl Base URL of the VillageOS Mycelium gateway
  --issuer      JWT issuer Mycelium signs with (default VillageOS)
  --audience    JWT audience Mycelium signs with (default VosClients)

Credentials come from the environment, never the command line:
  Token         Service JWT for authenticating to Mycelium (optional; else fetched)
  SigningKey    Base64 HMAC key for validating inbound /handle requests (optional)`;

// A credential is read from the environment alone. A command line is visible to every process on
// the host and is recorded by anything that logs the line a service was started with.
export function parseArgs(argv: string[], environment: NodeJS.ProcessEnv = process.env): Config | null {
  const map = new Map<string, string>();
  for (const a of argv) {
    const eq = a.indexOf("=");
    if (a.startsWith("--") && eq > 0) map.set(a.slice(0, eq), a.slice(eq + 1));
  }
  const portStr = map.get("--port");
  const myceliumUrl = map.get("--myceliumUrl");
  if (!portStr || !myceliumUrl) return null;
  const port = Number(portStr);
  if (!Number.isInteger(port) || port < 1 || port > 65535) return null;
  return {
    port,
    myceliumUrl: myceliumUrl.replace(/\/+$/, ""),
    token: environment.Token,
    signingKey: environment.SigningKey,
    issuer: map.get("--issuer") || "VillageOS",
    audience: map.get("--audience") || "VosClients",
  };
}

const handlerId = randomUUID();
let requestsProcessed = 0;

async function getToken(cfg: Config): Promise<string> {
  if (cfg.token) return cfg.token;
  const res = await fetch(`${cfg.myceliumUrl}/api/auth/token`, { method: "POST" });
  if (!res.ok) throw new Error(`token endpoint returned ${res.status}`);
  const body = (await res.json()) as { token: string };
  return body.token;
}

async function register(cfg: Config): Promise<void> {
  const token = await getToken(cfg);
  const base = `http://localhost:${cfg.port}`;
  const res = await fetch(`${cfg.myceliumUrl}/api/mycelium/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({
      handlerId,
      serviceName: SERVICE_NAME,
      endpointUrl: base,
      startCommand: "endpoint-service",
      stopEndpoint: `${base}/shutdown`,
      healthEndpoint: `${base}/health`,
    }),
  });
  if (!res.ok) throw new Error(`register returned ${res.status}`);
}

async function deregister(cfg: Config): Promise<void> {
  try {
    const token = await getToken(cfg);
    await fetch(`${cfg.myceliumUrl}/api/mycelium/services/${handlerId}`, {
      method: "DELETE",
      headers: { Authorization: `Bearer ${token}` },
    });
  } catch (err) {
    console.error("deregister failed:", err);
  }
}

// Write kinds — Facts, Observations, Sediment. docs/SERVICE_CONTRACT.md § "Writing data back".

export interface ObservationSample {
  property: string;
  value: unknown;
  observedAt?: string; // ISO-8601; omit to let Mycelium stamp now
}

export interface SedimentReading {
  thingId: string;
  property: string;
  value: unknown;
  observedAt: string; // required — sediment is historical
}

export interface SedimentResult {
  batchId: string;
  series: number;
  buckets: number;
  samples: number;
}

async function authedPost(cfg: Config, path: string, body: unknown): Promise<Response> {
  const token = await getToken(cfg);
  return fetch(`${cfg.myceliumUrl}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify(body),
  });
}

/** Assert a structural Fact; returns the commit sequence number (405 if ObservationOnly). */
export async function setFact(cfg: Config, thingId: string, property: string, value: unknown): Promise<number> {
  const res = await authedPost(cfg, `/api/things/${thingId}/properties/${encodeURIComponent(property)}/facts`, { value });
  if (res.status !== 201) throw new Error(`fact write returned ${res.status}`);
  const body = (await res.json()) as { sequenceNumber?: number };
  return body.sequenceNumber ?? 0;
}

/** Record one Observation (202); pass observedAt for late samples, omit for now (405 if FactOnly). */
export async function recordObservation(
  cfg: Config,
  thingId: string,
  property: string,
  value: unknown,
  observedAt?: string,
): Promise<void> {
  const body: Record<string, unknown> = { value };
  if (observedAt) body.observedAt = observedAt;
  const res = await authedPost(cfg, `/api/things/${thingId}/properties/${encodeURIComponent(property)}/observations`, body);
  if (!res.ok) throw new Error(`observation write returned ${res.status}`);
}

/** Record many samples across an entity's properties in one batch (202). Returns accepted count. */
export async function recordObservations(cfg: Config, thingId: string, samples: ObservationSample[]): Promise<number> {
  if (samples.length === 0) return 0;
  const res = await authedPost(cfg, `/api/things/${thingId}/observations`, samples);
  if (!res.ok) throw new Error(`observation batch returned ${res.status}`);
  const body = (await res.json()) as { accepted?: number };
  return body.accepted ?? samples.length;
}

/** Bulk-load historical readings to sealed Sapwood (202); entities must already exist. */
export async function depositSediment(cfg: Config, readings: SedimentReading[]): Promise<SedimentResult> {
  if (readings.length === 0) throw new Error("at least one reading is required");
  const res = await authedPost(cfg, `/api/sediment`, readings);
  if (!res.ok) throw new Error(`sediment deposit returned ${res.status}`);
  return (await res.json()) as SedimentResult;
}

export interface WriteKindsDemoResult {
  factSequence: number;
  observationsAccepted: number;
  sedimentBatchId: string;
  sedimentSamples: number;
}

/** Drive one of each write kind against an existing Thing. */
export async function demoWriteKinds(cfg: Config, thingId: string, now: Date = new Date()): Promise<WriteKindsDemoResult> {
  const iso = (d: Date) => d.toISOString();
  const factSequence = await setFact(cfg, thingId, "status", "active");
  await recordObservation(cfg, thingId, "temperature", 21.5, iso(now));
  const accepted = await recordObservations(cfg, thingId, [
    { property: "temperature", value: 21.7 },
    { property: "flow", value: 3.1 },
  ]);
  const dayAgo = new Date(now.getTime() - 86_400_000);
  const deposit = await depositSediment(cfg, [
    { thingId, property: "temperature", value: 19.8, observedAt: iso(dayAgo) },
    { thingId, property: "temperature", value: 20.4, observedAt: iso(new Date(dayAgo.getTime() + 3_600_000)) },
  ]);
  return {
    factSequence,
    observationsAccepted: accepted + 1,
    sedimentBatchId: deposit.batchId,
    sedimentSamples: deposit.samples,
  };
}

// Snapshot selector — subscribe to a slice (replaced launch-time IDs). docs/SERVICE_CONTRACT.md § "Selecting a slice".

export interface TraverseRule {
  predicate: string;
  direction?: "outgoing" | "incoming" | "both";
  depth?: number;
}

export interface Selector {
  all?: boolean;
  ids?: string[];
  names?: string[];
  types?: string[];
  traverse?: TraverseRule[];
}

export interface SubscribeResult {
  subscriptionId: string;
  watermark: number;
  snapshot: { things: { id: string; name?: string }[]; relationships: { id: string }[] };
}

/** A representative slice selector: every Thing of `type` plus its depth-1 `predicate` neighbours. */
export function sliceByTypeAndTraverse(type: string, predicate: string): Selector {
  return { types: [type], traverse: [{ predicate, direction: "outgoing", depth: 1 }] };
}

/** POST the selector to /api/subscriptions and return the resolved snapshot closure. */
export async function subscribe(cfg: Config, selector: Selector): Promise<SubscribeResult> {
  const res = await authedPost(cfg, "/api/subscriptions", selector);
  if (!res.ok) throw new Error(`subscribe returned ${res.status}`);
  return (await res.json()) as SubscribeResult;
}

/** Release a subscription (best-effort). */
export async function unsubscribe(cfg: Config, subscriptionId: string): Promise<void> {
  const token = await getToken(cfg);
  await fetch(`${cfg.myceliumUrl}/api/subscriptions/${subscriptionId}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });
}

export interface SelectorDemoResult {
  subscriptionId: string;
  watermark: number;
  things: number;
  relationships: number;
  thingNames: string[];
}

/** Runnable worked example: subscribe for a by-type+traverse slice, report the closure, unsubscribe. */
export async function demoSubscribe(cfg: Config, type = "Battery", predicate = "powers"): Promise<SelectorDemoResult> {
  const sub = await subscribe(cfg, sliceByTypeAndTraverse(type, predicate));
  const names = sub.snapshot.things.map((t) => t.name ?? t.id);
  await unsubscribe(cfg, sub.subscriptionId); // demo: release rather than stream
  return {
    subscriptionId: sub.subscriptionId,
    watermark: sub.watermark,
    things: sub.snapshot.things.length,
    relationships: sub.snapshot.relationships.length,
    thingNames: names,
  };
}

function b64urlToBuf(s: string): Buffer {
  return Buffer.from(s, "base64url");
}

// 30s clock skew matches ServiceTokenValidator on the .NET side.
export function verifyJwt(token: string, key: Buffer, issuer: string, audience: string): boolean {
  const parts = token.split(".");
  if (parts.length !== 3) return false;
  const [h, p, sig] = parts;
  const expected = createHmac("sha256", key).update(`${h}.${p}`).digest();
  const got = b64urlToBuf(sig);
  if (expected.length !== got.length || !timingSafeEqual(expected, got)) return false;

  let claims: { iss?: string; aud?: string | string[]; exp?: number; nbf?: number };
  try {
    claims = JSON.parse(b64urlToBuf(p).toString("utf8"));
  } catch {
    return false;
  }
  const now = Math.floor(Date.now() / 1000);
  const skew = 30;
  if (typeof claims.exp === "number" && now > claims.exp + skew) return false;
  if (typeof claims.nbf === "number" && now < claims.nbf - skew) return false;
  if (claims.iss !== issuer) return false;
  const aud = claims.aud;
  const audOk = Array.isArray(aud) ? aud.includes(audience) : aud === audience;
  return audOk;
}

function sendJson(res: ServerResponse, status: number, body: unknown): void {
  const json = JSON.stringify(body);
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(json);
}

function readBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    req.on("data", (c) => chunks.push(c as Buffer));
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

function authorized(req: IncomingMessage, cfg: Config, key: Buffer | null): boolean {
  if (!key) return true; // auth disabled when no signing key (matches .NET handlers)
  const auth = req.headers["authorization"];
  if (typeof auth !== "string" || !auth.startsWith("Bearer ")) return false;
  return verifyJwt(auth.slice("Bearer ".length), key, cfg.issuer, cfg.audience);
}

function main(): void {
  const cfg = parseArgs(process.argv.slice(2));
  if (!cfg) {
    console.error(USAGE);
    process.exit(1);
  }
  const key = cfg.signingKey ? Buffer.from(cfg.signingKey, "base64") : null;
  console.log(
    `VillageOS ${SERVICE_NAME} microservice — port ${cfg.port}, mycelium ${cfg.myceliumUrl}, auth=${key !== null}`,
  );

  const server = createServer(async (req, res) => {
    const url = req.url ?? "";
    const method = req.method ?? "GET";

    if (method === "GET" && url === "/health") {
      return sendJson(res, 200, { status: "Healthy", service: SERVICE_NAME, requestsProcessed });
    }
    if (method === "GET" && url === "/stats") {
      return sendJson(res, 200, {
        service: SERVICE_NAME,
        version: "1.0.0",
        requestsProcessed,
        handlerId,
        myceliumUrl: cfg.myceliumUrl,
      });
    }
    if (method === "POST" && url === "/handle") {
      if (!authorized(req, cfg, key)) return sendJson(res, 401, { error: "unauthorized" });
      const raw = await readBody(req);
      const n = ++requestsProcessed;
      let payload: Record<string, unknown> = {};
      try {
        payload = JSON.parse(raw);
      } catch {
        return sendJson(res, 400, { success: false, error: "invalid json" });
      }
      console.log(`handle #${n}: relationship ${payload["relationshipId"] ?? "?"}`);
      return sendJson(res, 200, {
        success: true,
        service: SERVICE_NAME,
        requestNumber: n,
        relationshipId: payload["relationshipId"] ?? null,
        status: "handled",
        echo: payload,
      });
    }
    if (method === "POST" && url === "/demo/write-kinds") {
      if (!authorized(req, cfg, key)) return sendJson(res, 401, { error: "unauthorized" });
      const raw = await readBody(req);
      let payload: { thingId?: string } = {};
      try {
        payload = JSON.parse(raw);
      } catch {
        return sendJson(res, 400, { error: "invalid json" });
      }
      if (!payload.thingId) return sendJson(res, 400, { error: "thingId is required" });
      try {
        return sendJson(res, 200, await demoWriteKinds(cfg, payload.thingId));
      } catch (err) {
        return sendJson(res, 500, { error: String(err) });
      }
    }
    if (method === "POST" && url === "/demo/subscribe") {
      if (!authorized(req, cfg, key)) return sendJson(res, 401, { error: "unauthorized" });
      let body: { type?: string; predicate?: string } = {};
      try {
        body = JSON.parse((await readBody(req)) || "{}");
      } catch {
        return sendJson(res, 400, { error: "invalid json" });
      }
      try {
        return sendJson(res, 200, await demoSubscribe(cfg, body.type ?? "Battery", body.predicate ?? "powers"));
      } catch (err) {
        return sendJson(res, 500, { error: String(err) });
      }
    }
    if (method === "POST" && url === "/shutdown") {
      if (!authorized(req, cfg, key)) return sendJson(res, 401, { error: "unauthorized" });
      sendJson(res, 200, { message: `Shutting down ${SERVICE_NAME} microservice` });
      setTimeout(() => void shutdown(cfg, server), 300);
      return;
    }
    sendJson(res, 404, { error: "not found" });
  });

  server.listen(cfg.port, "localhost", () => {
    void register(cfg)
      .then(() => console.log(`registered with mycelium as ${handlerId}`))
      .catch((err) => console.error("registration failed:", err));
  });

  const onSignal = () => void shutdown(cfg, server);
  process.on("SIGINT", onSignal);
  process.on("SIGTERM", onSignal);
}

async function shutdown(cfg: Config, server: ReturnType<typeof createServer>): Promise<void> {
  console.log(`shutting down — processed ${requestsProcessed} request(s)`);
  await deregister(cfg);
  server.close(() => process.exit(0));
}

// Only boot the server when run directly, so tests can import the pure helpers.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
