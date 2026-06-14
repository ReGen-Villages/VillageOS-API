// VillageOS managed-microservice example — Node.js + TypeScript (built-ins only).
//
// A managed microservice is a handler that Mycelium (the VillageOS gateway)
// launches as a daemon and calls when a relationship with the service's
// predicate is created. The whole contract is HTTP + a single HS256 JWT, and
// this file implements all of it using only Node's standard library
// (node:http, node:crypto, global fetch) — no runtime dependencies.
//
// This is an "echo" handler: POST /handle acknowledges the relationship and
// reflects the payload back. Replace handleRelationship() with real logic.
//
// `is` is NOT an external predicate — Mycelium handles `is` inheritance
// in-process and never dispatches it. Register for a custom predicate instead.

import { createServer, IncomingMessage, ServerResponse } from "node:http";
import { createHmac, randomUUID, timingSafeEqual } from "node:crypto";
import { pathToFileURL } from "node:url";

const SERVICE_NAME = "Node";

interface Config {
  port: number;
  myceliumUrl: string;
  token?: string;
  signingKey?: string;
  issuer: string;
  audience: string;
}

const USAGE = `Usage: node index.js --port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]
  --port        Port to listen on (1-65535)
  --myceliumUrl Base URL of the VillageOS Mycelium gateway
  --token       Service JWT for authenticating to Mycelium (optional; else fetched)
  --signingKey  Base64 HMAC key for validating inbound /handle requests (optional)
  --issuer      JWT issuer Mycelium signs with (default VillageOS)
  --audience    JWT audience Mycelium signs with (default VosClients)`;

export function parseArgs(argv: string[]): Config | null {
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
    token: map.get("--token"),
    signingKey: map.get("--signingKey"),
    issuer: map.get("--issuer") || "VillageOS",
    audience: map.get("--audience") || "VosClients",
  };
}

// ---- Mycelium registration ------------------------------------------------

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

// ---- inbound JWT validation (HS256) --------------------------------------

function b64urlToBuf(s: string): Buffer {
  return Buffer.from(s, "base64url");
}

// Validates an HS256 JWT against key/issuer/audience with 30s clock skew,
// matching ServiceTokenValidator on the .NET side. Returns true if valid.
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

// ---- HTTP plumbing --------------------------------------------------------

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
      // Real handlers do their predicate work here; the echo example acks + reflects.
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

// Only boot the server when run directly (so tests can import the pure helpers).
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main();
}
