//! VillageOS managed-microservice example — Rust + Axum.
//!
//! A managed microservice is a handler that Mycelium (the VillageOS gateway)
//! launches as a daemon and calls when a relationship with the service's
//! predicate is created. The whole contract is HTTP + a single JWT signed on the
//! P-256 elliptic curve (ES256). The key a handler holds checks a signature and
//! cannot produce one.
//!
//! `is` is NOT an external predicate — Mycelium handles `is` inheritance
//! in-process and never dispatches it. Register for a custom predicate instead.
//!
//! Run: ApiKey=<key> cargo run -- --port=5104 --myceliumUrl=https://localhost:7243

use std::sync::{
    atomic::{AtomicU64, Ordering},
    Arc,
};
use std::time::{SystemTime, UNIX_EPOCH};

use axum::{
    extract::{Request, State},
    http::StatusCode,
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::{get, post},
    Json, Router,
};
use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine};
use jsonwebtoken::{decode, Algorithm, DecodingKey, Validation};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

const SERVICE_NAME: &str = "Rust";

#[derive(Clone)]
struct Config {
    port: u16,
    mycelium_url: String,
    /// Exchanged with Mycelium for a short-lived JWT, and exchanged again before that runs out.
    api_key: Option<String>,
    /// A pre-minted service JWT, used when no key is given.
    token: Option<String>,
    /// The token the key was last exchanged for, shared by every copy of this configuration.
    held_token: Arc<tokio::sync::Mutex<Option<HeldToken>>>,
    /// Base64 of Mycelium's public signing key, for checking inbound requests.
    verification_key: Option<String>,
    #[allow(dead_code)]
    issuer: String,
    /// This service's own name. A token addressed to anything else is refused.
    audience: String,
}

struct HeldToken {
    token: String,
    expires_at_seconds: u64,
}

/// Parses the standard --key=value flags. Returns None if required ones missing.
///
/// A credential is read from the environment alone. A command line is visible to every process on
/// the host and is recorded by anything that logs the line a service was started with.
fn parse_args(args: &[String], environment: impl Fn(&str) -> Option<String>) -> Option<Config> {
    let mut port: Option<u16> = None;
    let mut mycelium_url: Option<String> = None;
    let api_key = environment("ApiKey").filter(|key| !key.is_empty());
    let token = environment("Token");
    let verification_key = environment("VerificationKey");
    let mut issuer = String::new();
    let mut audience = String::new();

    for a in args {
        let Some((k, v)) = a.split_once('=') else { continue };
        match k {
            "--port" => port = v.parse::<u16>().ok().filter(|p| *p >= 1),
            "--myceliumUrl" => mycelium_url = Some(v.trim_end_matches('/').to_string()),
            "--issuer" if !v.is_empty() => issuer = v.to_string(),
            "--audience" if !v.is_empty() => audience = v.to_string(),
            _ => {}
        }
    }

    // No default issuer or recipient name to fall back on. Each handler is addressed by its own
    // name, so a shared default could not be correct for anyone and would refuse every call.
    if verification_key.is_some() && (issuer.is_empty() || audience.is_empty()) {
        return None;
    }

    Some(Config {
        port: port?,
        mycelium_url: mycelium_url?,
        api_key,
        token,
        held_token: Default::default(),
        verification_key,
        issuer,
        audience,
    })
}

const USAGE: &str = "Usage: app --port=<port> --myceliumUrl=<url> [--issuer=<iss>] [--audience=<aud>]\n\
    --issuer and --audience are required whenever a VerificationKey is set; --audience is this service's own name.\n\
    Credentials come from the environment, never the command line: ApiKey, Token, VerificationKey.\n\
    An ApiKey is exchanged with Mycelium for a short-lived JWT and exchanged again before that runs out; a Token is used when no ApiKey is set.\n\
    With neither, the service answers its own routes and makes no call to Mycelium.";

struct AppState {
    config: Config,
    handler_id: String,
    requests: AtomicU64,
    stop_requested: tokio::sync::Notify,
    /// Built once: reading the machine's certificate store takes tens of milliseconds, which a
    /// client built for each request would pay every time.
    platform: reqwest::Client,
}

impl AppState {
    fn serving(config: Config) -> Self {
        AppState {
            config,
            handler_id: uuid::Uuid::new_v4().to_string(),
            requests: AtomicU64::new(0),
            stop_requested: tokio::sync::Notify::new(),
            platform: platform_client().expect("a client for calls to Mycelium"),
        }
    }
}

#[derive(Debug, Deserialize)]
#[allow(dead_code)]
struct Claims {
    iss: String,
    aud: String,
    exp: usize,
}

/// Wraps the base64 SubjectPublicKeyInfo encoding Mycelium hands out in the PEM envelope the
/// JWT library reads.
fn public_key_pem(base64_key: &str) -> String {
    let wrapped: Vec<&str> = base64_key.as_bytes().chunks(64)
        .map(|line| std::str::from_utf8(line).unwrap_or_default())
        .collect();
    format!(
        "-----BEGIN PUBLIC KEY-----\n{}\n-----END PUBLIC KEY-----\n",
        wrapped.join("\n"))
}

/// Validates the inbound JWT against Mycelium's public signing key, the issuer and this service's
/// own name, allowing 30s clock skew — matching ServiceTokenValidator (.NET).
///
/// The accepted algorithm is named rather than taken from the token. A checker that honoured the
/// token's own claim would accept a token signed with this public key used as a plain shared
/// secret, which is a value every handler holds.
fn verify_jwt(token: &str, base64_key: &str, issuer: &str, audience: &str) -> bool {
    let Ok(key) = DecodingKey::from_ec_pem(public_key_pem(base64_key).as_bytes()) else {
        return false;
    };
    let mut validation = Validation::new(Algorithm::ES256);
    validation.set_issuer(&[issuer]);
    validation.set_audience(&[audience]);
    validation.leeway = 30;
    decode::<Claims>(token, &key, &validation).is_ok()
}

/// Axum middleware: when a verification key is configured, require a valid
/// Mycelium-signed Bearer JWT. No-op otherwise (matches the .NET handlers).
async fn auth(State(state): State<Arc<AppState>>, req: Request, next: Next) -> Response {
    let Some(key) = state.config.verification_key.as_deref() else {
        return next.run(req).await;
    };
    let ok = req
        .headers()
        .get(axum::http::header::AUTHORIZATION)
        .and_then(|h| h.to_str().ok())
        .and_then(|h| h.strip_prefix("Bearer "))
        .map(|tok| verify_jwt(tok, key, &state.config.issuer, &state.config.audience))
        .unwrap_or(false);

    if ok {
        next.run(req).await
    } else {
        (StatusCode::UNAUTHORIZED, Json(json!({"error": "unauthorized"}))).into_response()
    }
}

async fn handle_relationship(State(state): State<Arc<AppState>>, body: Option<Json<Value>>) -> Response {
    let n = state.requests.fetch_add(1, Ordering::SeqCst) + 1;
    let payload = body.map(|Json(v)| v).unwrap_or(Value::Null);
    let rel_id = payload.get("relationshipId").cloned().unwrap_or(Value::Null);
    // Real handlers do their predicate work here; the echo example acks + reflects.
    println!("handle #{n}: relationship {rel_id}");
    Json(json!({
        "success": true,
        "service": SERVICE_NAME,
        "requestNumber": n,
        "relationshipId": rel_id,
        "status": "handled",
        "echo": payload,
    }))
    .into_response()
}

async fn health(State(state): State<Arc<AppState>>) -> Json<Value> {
    Json(json!({
        "status": "Healthy",
        "service": SERVICE_NAME,
        "requestsProcessed": state.requests.load(Ordering::SeqCst),
        "processId": std::process::id(),
    }))
}

async fn stats(State(state): State<Arc<AppState>>) -> Json<Value> {
    Json(json!({
        "service": SERVICE_NAME,
        "version": "1.0.0",
        "requestsProcessed": state.requests.load(Ordering::SeqCst),
        "handlerId": state.handler_id,
        "myceliumUrl": state.config.mycelium_url,
    }))
}

async fn shutdown(State(state): State<Arc<AppState>>) -> Json<Value> {
    state.stop_requested.notify_one();
    Json(json!({ "message": format!("Shutting down {SERVICE_NAME} microservice") }))
}

const NO_CREDENTIAL: &str = "neither ApiKey nor Token is set";

/// How long before a held token runs out it is replaced, so a call in flight never carries one
/// that expires on the way.
const REPLACEMENT_LEAD_SECONDS: u64 = 30;

/// A client for calls to Mycelium, which carry the key or a token. It checks the platform's
/// certificate against what this machine trusts, so a Mycelium on the same machine presenting the
/// development certificate is reached once that certificate is trusted, and anything else presenting
/// a certificate nothing vouches for is never sent the credential.
fn platform_client() -> reqwest::Result<reqwest::Client> {
    reqwest::Client::builder().build()
}

/// A failed call with every cause beneath it. The outermost says only that the request could not be
/// sent; why — a certificate refused, a connection refused — is further down.
fn reason_of(failure: reqwest::Error) -> String {
    let mut reason = failure.to_string();
    let mut cause = std::error::Error::source(&failure);
    while let Some(beneath) = cause {
        reason.push_str(": ");
        reason.push_str(&beneath.to_string());
        cause = beneath.source();
    }
    reason
}

async fn get_token(cfg: &Config, http: &reqwest::Client) -> Result<String, String> {
    let now = SystemTime::now().duration_since(UNIX_EPOCH).map_or(0, |since| since.as_secs());
    token_at(cfg, http, now).await
}

/// A key is presented before a token given beside it: a token runs out and the key can replace it.
async fn token_at(cfg: &Config, http: &reqwest::Client, now_seconds: u64) -> Result<String, String> {
    let Some(api_key) = &cfg.api_key else {
        return cfg.token.clone().ok_or_else(|| NO_CREDENTIAL.to_string());
    };

    let mut held = cfg.held_token.lock().await;
    if let Some(held) = held.as_ref().filter(|held| now_seconds + REPLACEMENT_LEAD_SECONDS < held.expires_at_seconds) {
        return Ok(held.token.clone());
    }

    let resp = http
        .post(format!("{}/api/auth/token", cfg.mycelium_url))
        .header("X-API-Key", api_key)
        .send()
        .await
        .map_err(reason_of)?;
    if !resp.status().is_success() {
        return Err(format!("mycelium refused to exchange the API key ({})", resp.status().as_u16()));
    }
    let body: Value = resp.json().await.map_err(|e| e.to_string())?;
    let token = body.get("token").and_then(Value::as_str).unwrap_or_default().to_string();
    let expires_at_seconds = expiry_of(&token)?;
    *held = Some(HeldToken { token: token.clone(), expires_at_seconds });
    Ok(token)
}

/// The token is Mycelium's own answer over the connection the key was sent on, so its expiry is
/// read without checking the signature.
fn expiry_of(token: &str) -> Result<u64, String> {
    let claims: Value = token
        .split('.')
        .nth(1)
        .and_then(|payload| URL_SAFE_NO_PAD.decode(payload).ok())
        .and_then(|payload| serde_json::from_slice(&payload).ok())
        .ok_or("mycelium answered the API key with something that is not a token")?;
    claims
        .get("exp")
        .and_then(Value::as_u64)
        .ok_or_else(|| "the token mycelium exchanged the API key for states no expiry, so it cannot be held".to_string())
}

/// Registers with Mycelium and says what happened. A call carrying no credential is always
/// refused, so with none the service makes no call.
async fn registration(state: &AppState) -> String {
    if state.config.api_key.is_none() && state.config.token.is_none() {
        return format!("{NO_CREDENTIAL}, so this service has not registered with mycelium");
    }
    match register(state).await {
        Ok(()) => format!("registered with mycelium as {}", state.handler_id),
        Err(reason) => format!("registration failed: {reason}"),
    }
}

async fn register(state: &AppState) -> Result<(), String> {
    let http = &state.platform;
    let token = get_token(&state.config, http).await?;
    let base = format!("http://localhost:{}", state.config.port);
    let payload = json!({
        "handlerId": state.handler_id,
        "serviceName": SERVICE_NAME,
        "endpointUrl": base,
        "startCommand": "endpoint-service",
        "stopEndpoint": format!("{base}/shutdown"),
        "healthEndpoint": format!("{base}/health"),
    });
    http.post(format!("{}/api/mycelium/register", state.config.mycelium_url))
        .bearer_auth(token)
        .json(&payload)
        .send()
        .await
        .and_then(reqwest::Response::error_for_status)
        .map_err(reason_of)?;
    Ok(())
}

// Write kinds — Facts, Observations, Sediment. docs/SERVICE_CONTRACT.md § "Writing data back".

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ObservationSample {
    property: String,
    value: Value,
    #[serde(skip_serializing_if = "Option::is_none")]
    observed_at: Option<String>, // ISO-8601; None lets Mycelium stamp now
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SedimentReading {
    object_id: String,
    property: String,
    value: Value,
    observed_at: String, // required — sediment is historical
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct SedimentResult {
    batch_id: String,
    #[allow(dead_code)]
    series: i64,
    #[allow(dead_code)]
    buckets: i64,
    samples: i64,
}

/// Percent-encode a single URL path segment (property names are usually safe, but be correct).
fn enc(s: &str) -> String {
    s.bytes()
        .map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => (b as char).to_string(),
            _ => format!("%{b:02X}"),
        })
        .collect()
}

/// Assert a structural Fact; return the commit sequence number (405 if ObservationOnly).
async fn set_fact(cfg: &Config, http: &reqwest::Client, thing_id: &str, property: &str, value: Value) -> Result<i64, String> {
    let token = get_token(cfg, http).await?;
    let url = format!("{}/api/things/{}/properties/{}/facts", cfg.mycelium_url, thing_id, enc(property));
    let resp = http.post(url).bearer_auth(token).json(&json!({ "value": value })).send().await.map_err(reason_of)?;
    if resp.status().as_u16() != 201 {
        return Err(format!("fact write returned {}", resp.status()));
    }
    let body: Value = resp.json().await.map_err(|e| e.to_string())?;
    Ok(body.get("sequenceNumber").and_then(Value::as_i64).unwrap_or(0))
}

/// Record one Observation (202); pass observed_at for late samples, None for now (405 if FactOnly).
async fn record_observation(cfg: &Config, http: &reqwest::Client, thing_id: &str, property: &str, value: Value, observed_at: Option<&str>) -> Result<(), String> {
    let token = get_token(cfg, http).await?;
    let mut body = json!({ "value": value });
    if let Some(at) = observed_at {
        body["observedAt"] = json!(at);
    }
    let url = format!("{}/api/things/{}/properties/{}/observations", cfg.mycelium_url, thing_id, enc(property));
    let resp = http.post(url).bearer_auth(token).json(&body).send().await.map_err(reason_of)?;
    if !resp.status().is_success() {
        return Err(format!("observation write returned {}", resp.status()));
    }
    Ok(())
}

/// Record many samples across an entity's properties in one batch (202). Returns accepted count.
async fn record_observations(cfg: &Config, http: &reqwest::Client, thing_id: &str, samples: &[ObservationSample]) -> Result<i64, String> {
    if samples.is_empty() {
        return Ok(0);
    }
    let token = get_token(cfg, http).await?;
    let url = format!("{}/api/things/{}/observations", cfg.mycelium_url, thing_id);
    let resp = http.post(url).bearer_auth(token).json(&samples).send().await.map_err(reason_of)?;
    if !resp.status().is_success() {
        return Err(format!("observation batch returned {}", resp.status()));
    }
    let body: Value = resp.json().await.map_err(|e| e.to_string())?;
    Ok(body.get("accepted").and_then(Value::as_i64).unwrap_or(samples.len() as i64))
}

/// Bulk-load historical readings to sealed Sapwood (202); entities must already exist.
async fn deposit_sediment(cfg: &Config, http: &reqwest::Client, readings: &[SedimentReading]) -> Result<SedimentResult, String> {
    if readings.is_empty() {
        return Err("at least one reading is required".to_string());
    }
    let token = get_token(cfg, http).await?;
    let url = format!("{}/api/sediment", cfg.mycelium_url);
    let resp = http.post(url).bearer_auth(token).json(&readings).send().await.map_err(reason_of)?;
    if !resp.status().is_success() {
        return Err(format!("sediment deposit returned {}", resp.status()));
    }
    resp.json::<SedimentResult>().await.map_err(|e| e.to_string())
}

#[derive(Deserialize)]
struct DemoReq {
    #[serde(rename = "thingId")]
    thing_id: Option<String>,
}

/// Drive one of each write kind against an existing Thing (fixed timestamps keep it dependency-free).
async fn demo_write_kinds(State(state): State<Arc<AppState>>, body: Option<Json<DemoReq>>) -> Response {
    let thing_id = match body.and_then(|Json(b)| b.thing_id) {
        Some(t) if !t.is_empty() => t,
        _ => return (StatusCode::BAD_REQUEST, Json(json!({ "error": "thingId is required" }))).into_response(),
    };
    let http = &state.platform;
    let cfg = &state.config;

    let run = async {
        let seq = set_fact(cfg, http, &thing_id, "status", json!("active")).await?;
        record_observation(cfg, http, &thing_id, "temperature", json!(21.5), Some("2026-06-20T12:00:00Z")).await?;
        let accepted = record_observations(cfg, http, &thing_id, &[
            ObservationSample { property: "temperature".into(), value: json!(21.7), observed_at: None },
            ObservationSample { property: "flow".into(), value: json!(3.1), observed_at: None },
        ]).await?;
        let deposit = deposit_sediment(cfg, http, &[
            SedimentReading { object_id: thing_id.clone(), property: "temperature".into(), value: json!(19.8), observed_at: "2026-06-19T12:00:00Z".into() },
            SedimentReading { object_id: thing_id.clone(), property: "temperature".into(), value: json!(20.4), observed_at: "2026-06-19T13:00:00Z".into() },
        ]).await?;
        Ok::<_, String>(json!({
            "factSequence": seq,
            "observationsAccepted": accepted + 1,
            "sedimentBatchId": deposit.batch_id,
            "sedimentSamples": deposit.samples,
        }))
    };

    match run.await {
        Ok(v) => Json(v).into_response(),
        Err(e) => (StatusCode::INTERNAL_SERVER_ERROR, Json(json!({ "error": e }))).into_response(),
    }
}

// Snapshot selector — subscribe to a slice (replaced launch-time IDs). docs/SERVICE_CONTRACT.md § "Selecting a slice".

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct TraverseRule {
    predicate: String,
    direction: String,
    depth: i64,
}

#[derive(Serialize, Default)]
#[serde(rename_all = "camelCase")]
struct Selector {
    #[serde(skip_serializing_if = "Option::is_none")]
    types: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    ids: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    names: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    traverse: Option<Vec<TraverseRule>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    all: Option<bool>,
}

// Mycelium answers each Thing and relationship with capitalised field names.
#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct SnapThing {
    id: String,
    name: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "PascalCase")]
struct SnapRel {
    #[allow(dead_code)]
    id: String,
}

#[derive(Deserialize)]
struct Snapshot {
    things: Vec<SnapThing>,
    relationships: Vec<SnapRel>,
}

fn names_of(snapshot: &Snapshot) -> Vec<String> {
    snapshot.things.iter().map(|t| t.name.clone().unwrap_or_else(|| t.id.clone())).collect()
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct SubscribeResult {
    subscription_id: String,
    watermark: i64,
    snapshot: Snapshot,
}

/// A representative slice: every Thing of `type_` plus its depth-1 `predicate` neighbours.
fn slice_by_type_and_traverse(type_: &str, predicate: &str) -> Selector {
    Selector {
        types: Some(vec![type_.to_string()]),
        traverse: Some(vec![TraverseRule {
            predicate: predicate.to_string(),
            direction: "outgoing".into(),
            depth: 1,
        }]),
        ..Default::default()
    }
}

/// POST the selector to /api/subscriptions; return the resolved snapshot closure.
async fn subscribe(cfg: &Config, http: &reqwest::Client, selector: &Selector) -> Result<SubscribeResult, String> {
    let token = get_token(cfg, http).await?;
    let url = format!("{}/api/subscriptions", cfg.mycelium_url);
    let resp = http.post(url).bearer_auth(token).json(selector).send().await.map_err(reason_of)?;
    if !resp.status().is_success() {
        return Err(format!("subscribe returned {}", resp.status()));
    }
    resp.json::<SubscribeResult>().await.map_err(|e| e.to_string())
}

/// Release a subscription (best-effort).
async fn unsubscribe(cfg: &Config, http: &reqwest::Client, id: &str) {
    if let Ok(token) = get_token(cfg, http).await {
        let _ = http
            .delete(format!("{}/api/subscriptions/{}", cfg.mycelium_url, id))
            .bearer_auth(token)
            .send()
            .await;
    }
}

#[derive(Deserialize)]
struct SubDemoReq {
    #[serde(rename = "type")]
    type_: Option<String>,
    predicate: Option<String>,
}

/// Subscribe for a slice and report its closure. POST optional {"type":"...","predicate":"..."}.
async fn demo_subscribe(State(state): State<Arc<AppState>>, body: Option<Json<SubDemoReq>>) -> Response {
    let (type_, predicate) = body.map(|Json(b)| (b.type_, b.predicate)).unwrap_or((None, None));
    let type_ = type_.unwrap_or_else(|| "Battery".into());
    let predicate = predicate.unwrap_or_else(|| "powers".into());
    let http = &state.platform;
    let cfg = &state.config;
    match subscribe(cfg, http, &slice_by_type_and_traverse(&type_, &predicate)).await {
        Ok(sub) => {
            let names = names_of(&sub.snapshot);
            unsubscribe(cfg, http, &sub.subscription_id).await;
            Json(json!({
                "subscriptionId": sub.subscription_id,
                "watermark": sub.watermark,
                "things": sub.snapshot.things.len(),
                "relationships": sub.snapshot.relationships.len(),
                "thingNames": names,
            }))
            .into_response()
        }
        Err(e) => (StatusCode::INTERNAL_SERVER_ERROR, Json(json!({ "error": e }))).into_response(),
    }
}

#[tokio::main]
async fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let Some(config) = parse_args(&args, |name| std::env::var(name).ok()) else {
        eprintln!("{USAGE}");
        std::process::exit(1);
    };

    let port = config.port;
    let auth_enabled = config.verification_key.is_some();
    let state = Arc::new(AppState::serving(config));
    println!(
        "VillageOS {SERVICE_NAME} microservice — port {port}, mycelium {}, auth={auth_enabled}",
        state.config.mycelium_url
    );

    // Register once the listener is bound.
    let reg_state = state.clone();
    tokio::spawn(async move {
        tokio::time::sleep(std::time::Duration::from_millis(200)).await;
        println!("{}", registration(&reg_state).await);
    });

    let listener = tokio::net::TcpListener::bind(("127.0.0.1", port))
        .await
        .expect("bind");

    serve_until_stopped(listener, state).await;
}

async fn serve_until_stopped(listener: tokio::net::TcpListener, state: Arc<AppState>) {
    // /handle and /shutdown are auth-protected; /health and /stats are open.
    let protected = Router::new()
        .route("/handle", post(handle_relationship))
        .route("/demo/write-kinds", post(demo_write_kinds))
        .route("/demo/subscribe", post(demo_subscribe))
        .route("/shutdown", post(shutdown))
        .layer(middleware::from_fn_with_state(state.clone(), auth));
    let app = Router::new()
        .route("/health", get(health))
        .route("/stats", get(stats))
        .merge(protected)
        .with_state(state.clone());

    axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            tokio::select! {
                _ = tokio::signal::ctrl_c() => {}
                _ = state.stop_requested.notified() => {}
            }
            on_shutdown(&state).await;
        })
        .await
        .expect("server");
}

async fn on_shutdown(state: &AppState) {
    println!(
        "shutting down — processed {} request(s)",
        state.requests.load(Ordering::SeqCst)
    );
}

#[cfg(test)]
mod tests {
    use super::*;
    use base64::{engine::general_purpose::STANDARD as B64, Engine};
    use jsonwebtoken::{encode, EncodingKey, Header};
    use serde::Serialize;

    const THIS_HANDLER: &str = "rust-echo-handler";

    /// A throwaway P-256 pair generated for these tests alone, standing in for Mycelium's. Held as
    /// base64 of the DER encodings, the same shape everything else in this file uses.
    const MYCELIUM_PRIVATE_KEY: &str = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgV9WJFCZHey9GDpFAnXa6AzS2fyCsmDP+mrBN7H1fRT+hRANCAARtmT5X4JQdbx0PJA2zt1PZEPpvsBfQ0HGgomTeM9eOG+kePwktwPMO9LC07x81YCnqhZzLeE/XLq92DskNgQr5";

    const MYCELIUM_PUBLIC_KEY: &str = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEbZk+V+CUHW8dDyQNs7dT2RD6b7AX0NBxoKJk3jPXjhvpHj8JLcDzDvSwtO8fNWAp6oWcy3hP1y6vdg7JDYEK+Q==";

    /// The public half, as a daemon receives it.
    fn b64_key() -> String {
        MYCELIUM_PUBLIC_KEY.to_string()
    }

    fn private_key_pem() -> String {
        let wrapped: Vec<&str> = MYCELIUM_PRIVATE_KEY.as_bytes().chunks(64)
            .map(|line| std::str::from_utf8(line).unwrap())
            .collect();
        format!(
            "-----BEGIN PRIVATE KEY-----\n{}\n-----END PRIVATE KEY-----\n",
            wrapped.join("\n"))
    }

    #[derive(Serialize)]
    struct TestClaims {
        iss: String,
        aud: String,
        sub: String,
        exp: usize,
        nbf: usize,
        iat: usize,
    }

    fn make_token(iss: &str, aud: &str, exp_offset: i64) -> String {
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_secs() as i64;
        let claims = TestClaims {
            iss: iss.to_string(),
            aud: aud.to_string(),
            sub: "mycelium".to_string(),
            exp: (now + exp_offset) as usize,
            nbf: now as usize,
            iat: now as usize,
        };
        let key = EncodingKey::from_ec_pem(private_key_pem().as_bytes()).unwrap();
        encode(&Header::new(Algorithm::ES256), &claims, &key).unwrap()
    }

    /// What someone who reads the verification key off a daemon can produce: a token signed with
    /// the public half used as a plain shared secret.
    fn forged_from_the_verification_key(iss: &str, aud: &str) -> String {
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_secs() as i64;
        let claims = TestClaims {
            iss: iss.to_string(),
            aud: aud.to_string(),
            sub: "mycelium".to_string(),
            exp: (now + 60) as usize,
            nbf: now as usize,
            iat: now as usize,
        };
        let secret = B64.decode(MYCELIUM_PUBLIC_KEY).unwrap();
        encode(&Header::new(Algorithm::HS256), &claims, &EncodingKey::from_secret(&secret)).unwrap()
    }

    fn empty_environment(_: &str) -> Option<String> {
        None
    }

    #[test]
    fn parse_args_valid_and_defaults_nothing() {
        let cfg = parse_args(
            &[
                "--port=5104".into(),
                "--myceliumUrl=https://localhost:7243/".into(),
            ],
            empty_environment,
        )
        .unwrap();
        assert_eq!(cfg.port, 5104);
        assert_eq!(cfg.mycelium_url, "https://localhost:7243");
        assert_eq!(cfg.issuer, "");
        assert_eq!(cfg.audience, "");
    }

    /// Each handler is addressed by its own name, so there is no shared default left that could
    /// be right.
    #[test]
    fn parse_args_refuses_a_verification_key_with_no_recipient_name() {
        let with_key = |name: &str| match name {
            "VerificationKey" => Some("ZW52aXJvbm1lbnQta2V5".to_string()),
            _ => None,
        };
        assert!(parse_args(
            &["--port=5104".into(), "--myceliumUrl=https://x".into(), "--issuer=VillageOS".into()],
            with_key).is_none());
        assert!(parse_args(
            &["--port=5104".into(), "--myceliumUrl=https://x".into(),
              format!("--audience={THIS_HANDLER}")],
            with_key).is_none());
    }

    #[test]
    fn parse_args_missing_required_returns_none() {
        assert!(parse_args(&["--port=5104".into()], empty_environment).is_none());
        assert!(parse_args(&["--myceliumUrl=x".into()], empty_environment).is_none());
    }

    #[test]
    fn parse_args_takes_credentials_from_the_environment() {
        let cfg = parse_args(
            &["--port=5104".into(), "--myceliumUrl=https://localhost:7243".into(),
              "--issuer=VillageOS".into(), format!("--audience={THIS_HANDLER}")],
            |name| match name {
                "Token" => Some("environment-token".into()),
                "VerificationKey" => Some("ZW52aXJvbm1lbnQta2V5".into()),
                _ => None,
            },
        )
        .unwrap();
        assert_eq!(cfg.token.as_deref(), Some("environment-token"));
        assert_eq!(cfg.verification_key.as_deref(), Some("ZW52aXJvbm1lbnQta2V5"));
    }

    #[test]
    fn parse_args_ignores_credentials_given_as_flags() {
        let cfg = parse_args(
            &[
                "--port=5104".into(),
                "--myceliumUrl=https://localhost:7243".into(),
                "--token=flag-token".into(),
                "--verificationKey=flag-key".into(),
            ],
            empty_environment,
        )
        .unwrap();
        assert!(cfg.token.is_none());
        assert!(cfg.verification_key.is_none());
    }

    #[test]
    fn verify_jwt_accepts_valid() {
        let tok = make_token("VillageOS", THIS_HANDLER, 60);
        assert!(verify_jwt(&tok, &b64_key(), "VillageOS", THIS_HANDLER));
    }

    #[test]
    fn verify_jwt_rejects_tampered() {
        let tok = make_token("VillageOS", THIS_HANDLER, 60) + "x";
        assert!(!verify_jwt(&tok, &b64_key(), "VillageOS", THIS_HANDLER));
    }

    #[test]
    fn verify_jwt_rejects_expired() {
        let tok = make_token("VillageOS", THIS_HANDLER, -120);
        assert!(!verify_jwt(&tok, &b64_key(), "VillageOS", THIS_HANDLER));
    }

    #[test]
    fn verify_jwt_rejects_wrong_issuer_and_another_services_name() {
        let bad_iss = make_token("Attacker", THIS_HANDLER, 60);
        let elsewhere = make_token("VillageOS", "spring-handler", 60);
        let a_persons_browser_token = make_token("VillageOS", "VosClients", 60);
        assert!(!verify_jwt(&bad_iss, &b64_key(), "VillageOS", THIS_HANDLER));
        assert!(!verify_jwt(&elsewhere, &b64_key(), "VillageOS", THIS_HANDLER));
        assert!(!verify_jwt(&a_persons_browser_token, &b64_key(), "VillageOS", THIS_HANDLER));
    }

    /// Every handler holds the verification key. A checker that honoured the algorithm the token
    /// names would let any of them sign one.
    #[test]
    fn verify_jwt_rejects_the_verification_key_used_as_a_shared_secret() {
        let forged = forged_from_the_verification_key("VillageOS", THIS_HANDLER);
        assert!(!verify_jwt(&forged, &b64_key(), "VillageOS", THIS_HANDLER));
    }

    // ---- Write kinds (Fact / Observation / Sediment) ----
    // Spin up an in-process Axum mock Mycelium (no extra dependencies) and assert the write
    // helpers hit the right routes, carry the bearer token, and parse the responses.

    use axum::http::header::AUTHORIZATION;
    use std::sync::Mutex;

    type Captured = Arc<Mutex<Vec<(String, String, String)>>>; // (path, auth, body)

    async fn mock_handler(State(cap): State<Captured>, req: Request) -> Response {
        let path = req.uri().path().to_string();
        let auth = req
            .headers()
            .get(AUTHORIZATION)
            .and_then(|h| h.to_str().ok())
            .unwrap_or("")
            .to_string();
        let bytes = axum::body::to_bytes(req.into_body(), usize::MAX).await.unwrap_or_default();
        let body = String::from_utf8_lossy(&bytes).to_string();
        cap.lock().unwrap().push((path.clone(), auth, body));

        if path.ends_with("/facts") {
            (StatusCode::CREATED, Json(json!({ "sequenceNumber": 42, "value": "active" }))).into_response()
        } else if path.contains("/properties/") && path.ends_with("/observations") {
            StatusCode::ACCEPTED.into_response()
        } else if path.ends_with("/observations") {
            (StatusCode::ACCEPTED, Json(json!({ "accepted": 2 }))).into_response()
        } else if path.ends_with("/sediment") {
            (StatusCode::ACCEPTED, Json(json!({ "batchId": "b-1", "series": 1, "buckets": 3, "samples": 10 }))).into_response()
        } else {
            StatusCode::NOT_FOUND.into_response()
        }
    }

    async fn spawn_mock(cap: Captured) -> String {
        let app = Router::new().fallback(mock_handler).with_state(cap);
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        tokio::spawn(async move { axum::serve(listener, app).await.unwrap() });
        format!("http://{addr}")
    }

    fn test_cfg(url: String) -> Config {
        Config {
            port: 0,
            mycelium_url: url,
            api_key: None,
            token: Some("tok".into()),
            held_token: Default::default(),
            verification_key: None,
            issuer: "VillageOS".into(),
            audience: "VosClients".into(),
        }
    }

    #[tokio::test]
    async fn write_kinds_hit_the_right_routes() {
        let cap: Captured = Arc::new(Mutex::new(Vec::new()));
        let cfg = test_cfg(spawn_mock(cap.clone()).await);
        let http = reqwest::Client::new();

        let seq = set_fact(&cfg, &http, "t1", "status", json!("active")).await.unwrap();
        assert_eq!(seq, 42);
        record_observation(&cfg, &http, "t1", "temperature", json!(21.5), Some("2026-06-20T14:00:00Z")).await.unwrap();
        let n = record_observations(&cfg, &http, "t1", &[
            ObservationSample { property: "temperature".into(), value: json!(21.7), observed_at: None },
            ObservationSample { property: "flow".into(), value: json!(3.1), observed_at: None },
        ]).await.unwrap();
        assert_eq!(n, 2);
        let res = deposit_sediment(&cfg, &http, &[
            SedimentReading { object_id: "t1".into(), property: "flow".into(), value: json!(1.0), observed_at: "2026-06-19T00:00:00Z".into() },
        ]).await.unwrap();
        assert_eq!(res.batch_id, "b-1");
        assert_eq!(res.samples, 10);

        let calls = cap.lock().unwrap();
        let paths: Vec<&str> = calls.iter().map(|(p, _, _)| p.as_str()).collect();
        assert!(paths.contains(&"/api/things/t1/properties/status/facts"));
        assert!(paths.contains(&"/api/things/t1/properties/temperature/observations"));
        assert!(paths.contains(&"/api/things/t1/observations"));
        assert!(paths.contains(&"/api/sediment"));
        assert!(calls.iter().all(|(_, a, _)| a == "Bearer tok"));
        let sed = calls.iter().find(|(p, _, _)| p == "/api/sediment").unwrap();
        assert!(sed.2.contains("observedAt"));
        assert!(sed.2.contains("\"objectId\":\"t1\""), "the sediment route reads the Thing from objectId, and the body is {}", sed.2);
    }

    #[tokio::test]
    async fn set_fact_errors_on_405() {
        async fn always_405() -> Response {
            StatusCode::METHOD_NOT_ALLOWED.into_response()
        }
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        tokio::spawn(async move { axum::serve(listener, Router::<()>::new().fallback(always_405)).await.unwrap() });

        let cfg = test_cfg(format!("http://{addr}"));
        let err = set_fact(&cfg, &reqwest::Client::new(), "t1", "temperature", json!(1)).await.unwrap_err();
        assert!(err.contains("405"));
    }

    #[tokio::test]
    async fn empty_batches_short_circuit() {
        let cfg = test_cfg("http://127.0.0.1:1".into()); // never contacted
        let http = reqwest::Client::new();
        assert_eq!(record_observations(&cfg, &http, "t1", &[]).await.unwrap(), 0);
        assert!(deposit_sediment(&cfg, &http, &[]).await.is_err());
    }

    type Cap = Arc<Mutex<Vec<(String, String, String)>>>;

    async fn sub_mock(State(cap): State<Cap>, req: Request) -> Response {
        let path = req.uri().path().to_string();
        let auth = req.headers().get(AUTHORIZATION).and_then(|h| h.to_str().ok()).unwrap_or("").to_string();
        let bytes = axum::body::to_bytes(req.into_body(), usize::MAX).await.unwrap_or_default();
        let body = String::from_utf8_lossy(&bytes).to_string();
        cap.lock().unwrap().push((path.clone(), auth, body));
        if path == "/api/subscriptions" {
            (StatusCode::OK, Json(json!({
                "subscriptionId": "s-1",
                "watermark": 42,
                "snapshot": {
                    "things": [{ "Id": "t1", "Name": "Battery-1" }, { "Id": "t2", "Name": null }],
                    "relationships": [{ "Id": "r1", "Name": null }]
                }
            }))).into_response()
        } else {
            StatusCode::OK.into_response()
        }
    }

    #[test]
    fn slice_by_type_and_traverse_builds_selector() {
        let body = serde_json::to_string(&slice_by_type_and_traverse("Battery", "powers")).unwrap();
        assert!(body.contains("\"types\"") && body.contains("Battery"));
        assert!(body.contains("\"traverse\"") && body.contains("powers"));
        assert!(!body.contains("\"all\"")); // unset fields omitted
    }

    // The deregistration route is admin-only; the broker's liveness monitor removes a registration whose service stops answering.
    #[tokio::test]
    async fn shutdown_does_not_ask_the_broker_to_withdraw() {
        let cap: Cap = Arc::new(Mutex::new(Vec::new()));
        let app = Router::new().fallback(sub_mock).with_state(cap.clone());
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        tokio::spawn(async move { axum::serve(listener, app).await.unwrap() });
        let state = AppState::serving(test_cfg(format!("http://{addr}")));

        on_shutdown(&state).await;

        let calls = cap.lock().unwrap();
        assert!(calls.is_empty(), "a service cannot deregister itself, so shutdown must not try; broker received {calls:?}");
    }

    #[tokio::test]
    async fn subscribe_posts_selector_and_returns_closure() {
        let cap: Cap = Arc::new(Mutex::new(Vec::new()));
        let app = Router::new().fallback(sub_mock).with_state(cap.clone());
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        tokio::spawn(async move { axum::serve(listener, app).await.unwrap() });

        let cfg = test_cfg(format!("http://{addr}"));
        let http = reqwest::Client::new();
        let sub = subscribe(&cfg, &http, &slice_by_type_and_traverse("Battery", "powers")).await.unwrap();
        assert_eq!(sub.subscription_id, "s-1");
        assert_eq!(names_of(&sub.snapshot), ["Battery-1", "t2"], "a Thing with no name is shown by its identifier");
        assert_eq!(sub.snapshot.relationships.len(), 1);
        unsubscribe(&cfg, &http, &sub.subscription_id).await;

        let calls = cap.lock().unwrap();
        assert_eq!(calls[0].0, "/api/subscriptions");
        assert_eq!(calls[0].1, "Bearer tok");
        assert!(calls[0].2.contains("types") && calls[0].2.contains("Battery") && calls[0].2.contains("powers"));
        assert_eq!(calls[1].0, "/api/subscriptions/s-1");
    }

    // ---- The demo routes, on the client the service holds ----

    async fn answered(answer: Response) -> (StatusCode, Value) {
        let status = answer.status();
        let body = axum::body::to_bytes(answer.into_body(), usize::MAX).await.unwrap();
        (status, serde_json::from_slice(&body).unwrap())
    }

    #[tokio::test]
    async fn the_write_kinds_route_makes_each_write_and_answers_what_the_platform_said() {
        let cap: Captured = Arc::new(Mutex::new(Vec::new()));
        let state = Arc::new(AppState::serving(test_cfg(spawn_mock(cap.clone()).await)));
        let thing: DemoReq = serde_json::from_value(json!({ "thingId": "t1" })).unwrap();

        let (status, said) = answered(demo_write_kinds(State(state), Some(Json(thing))).await).await;

        assert_eq!(status, StatusCode::OK, "{said}");
        assert_eq!(said, json!({ "factSequence": 42, "observationsAccepted": 3, "sedimentBatchId": "b-1", "sedimentSamples": 10 }));
        assert_eq!(cap.lock().unwrap().len(), 4);
    }

    #[tokio::test]
    async fn the_subscribe_route_answers_the_slice_and_gives_the_subscription_up() {
        let cap: Cap = Arc::new(Mutex::new(Vec::new()));
        let app = Router::new().fallback(sub_mock).with_state(cap.clone());
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        tokio::spawn(async move { axum::serve(listener, app).await.unwrap() });
        let state = Arc::new(AppState::serving(test_cfg(format!("http://{addr}"))));

        let (status, said) = answered(demo_subscribe(State(state), None).await).await;

        assert_eq!(status, StatusCode::OK, "{said}");
        assert_eq!(said["subscriptionId"], "s-1");
        assert_eq!(said["thingNames"], json!(["Battery-1", "t2"]));
        let calls = cap.lock().unwrap();
        assert_eq!(calls.last().unwrap().0, "/api/subscriptions/s-1", "the subscription was left open");
    }

    // ---- Presenting a key ----

    use base64::engine::general_purpose::URL_SAFE_NO_PAD;

    const A_MOMENT: u64 = 1_790_942_400;
    const FIVE_MINUTES: u64 = 300;

    fn token_with_claims(claims: Value) -> String {
        let part = |text: String| URL_SAFE_NO_PAD.encode(text);
        format!("{}.{}.{}", part(json!({ "alg": "ES256" }).to_string()), part(claims.to_string()), part("not-a-signature".into()))
    }

    fn token_expiring_at(seconds: u64) -> String {
        token_with_claims(json!({ "sub": "a-service", "exp": seconds }))
    }

    /// Stands in for the route that exchanges a key, and records the key each call presented.
    #[derive(Clone)]
    struct KeyExchange {
        keys_presented: Arc<Mutex<Vec<String>>>,
        answer: Arc<Mutex<(StatusCode, String)>>,
    }

    impl KeyExchange {
        fn answering(status: StatusCode, token: String) -> Self {
            KeyExchange { keys_presented: Default::default(), answer: Arc::new(Mutex::new((status, token))) }
        }

        async fn serve(&self) -> Config {
            async fn exchange(State(exchange): State<KeyExchange>, req: Request) -> Response {
                assert_eq!(req.uri().path(), "/api/auth/token");
                let key = req.headers().get("X-API-Key").and_then(|h| h.to_str().ok()).unwrap_or("").to_string();
                exchange.keys_presented.lock().unwrap().push(key);
                let (status, token) = exchange.answer.lock().unwrap().clone();
                (status, Json(json!({ "token": token }))).into_response()
            }
            let app = Router::new().fallback(exchange).with_state(self.clone());
            let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
            let addr = listener.local_addr().unwrap();
            tokio::spawn(async move { axum::serve(listener, app).await.unwrap() });
            Config { api_key: Some("vos_ak_example".into()), token: None, ..test_cfg(format!("http://{addr}")) }
        }
    }

    #[test]
    fn parse_args_reads_an_api_key_from_the_environment_and_never_from_a_flag() {
        let flags: [String; 3] = ["--port=5104".into(), "--myceliumUrl=https://localhost:7243".into(), "--apiKey=flag-key".into()];
        let from_the_environment = parse_args(&flags, |name| (name == "ApiKey").then(|| "environment-key".to_string())).unwrap();
        let from_a_flag = parse_args(&flags, empty_environment).unwrap();

        assert_eq!(from_the_environment.api_key.as_deref(), Some("environment-key"));
        assert!(from_a_flag.api_key.is_none());
    }

    #[tokio::test]
    async fn a_key_is_exchanged_in_the_key_header_and_the_token_is_held() {
        let minted = token_expiring_at(A_MOMENT + FIVE_MINUTES);
        let exchange = KeyExchange::answering(StatusCode::OK, minted.clone());
        let cfg = exchange.serve().await;
        let http = reqwest::Client::new();

        for _ in 0..3 {
            assert_eq!(token_at(&cfg, &http, A_MOMENT).await.unwrap(), minted);
        }

        assert_eq!(*exchange.keys_presented.lock().unwrap(), ["vos_ak_example"]);
    }

    #[tokio::test]
    async fn a_held_token_is_exchanged_again_shortly_before_it_runs_out() {
        let exchange = KeyExchange::answering(StatusCode::OK, String::new());
        let cfg = exchange.serve().await;
        let http = reqwest::Client::new();

        let mut now = A_MOMENT;
        for _ in 0..3 {
            exchange.answer.lock().unwrap().1 = token_expiring_at(now + FIVE_MINUTES);
            token_at(&cfg, &http, now).await.unwrap();
            now += FIVE_MINUTES - 20;
        }

        assert_eq!(
            exchange.keys_presented.lock().unwrap().len(), 3,
            "twenty seconds from running out is inside the half minute a token is replaced in");
    }

    #[tokio::test]
    async fn a_token_that_states_no_expiry_is_not_held() {
        let exchange = KeyExchange::answering(StatusCode::OK, token_with_claims(json!({ "sub": "a-service" })));
        let cfg = exchange.serve().await;

        let refusal = token_at(&cfg, &reqwest::Client::new(), A_MOMENT).await.unwrap_err();

        assert!(refusal.contains("no expiry"), "{refusal}");
    }

    #[tokio::test]
    async fn a_refused_key_says_what_the_platform_answered() {
        let exchange = KeyExchange::answering(StatusCode::UNAUTHORIZED, String::new());
        let cfg = exchange.serve().await;

        let refusal = token_at(&cfg, &reqwest::Client::new(), A_MOMENT).await.unwrap_err();

        assert!(refusal.contains("refused to exchange the API key") && refusal.contains("401"), "{refusal}");
    }

    #[tokio::test]
    async fn an_answer_that_is_not_a_token_is_not_held() {
        let exchange = KeyExchange::answering(StatusCode::OK, "one.!!!.three".into());
        let cfg = exchange.serve().await;

        let refusal = token_at(&cfg, &reqwest::Client::new(), A_MOMENT).await.unwrap_err();

        assert!(refusal.contains("not a token"), "{refusal}");
    }

    #[tokio::test]
    async fn with_neither_a_key_nor_a_token_there_is_nothing_to_present() {
        let cfg = Config { token: None, ..test_cfg("http://127.0.0.1:1".into()) };

        let refusal = token_at(&cfg, &reqwest::Client::new(), A_MOMENT).await.unwrap_err();

        assert_eq!(refusal, NO_CREDENTIAL);
    }

    #[tokio::test]
    async fn a_key_is_presented_before_a_token_given_beside_it() {
        let minted = token_expiring_at(A_MOMENT + FIVE_MINUTES);
        let exchange = KeyExchange::answering(StatusCode::OK, minted.clone());
        let cfg = Config { token: Some("a-launch-token".into()), ..exchange.serve().await };

        assert_eq!(token_at(&cfg, &reqwest::Client::new(), A_MOMENT).await.unwrap(), minted);
    }

    #[tokio::test]
    async fn with_neither_a_key_nor_a_token_no_call_is_made_and_the_service_says_so() {
        let cap: Captured = Arc::new(Mutex::new(Vec::new()));
        let cfg = Config { token: None, ..test_cfg(spawn_mock(cap.clone()).await) };

        let said = registration(&AppState::serving(cfg)).await;

        assert!(cap.lock().unwrap().is_empty(), "a call carrying no credential is always refused");
        assert!(said.contains("ApiKey") && said.contains("Token"), "{said}");
    }

    #[test]
    fn usage_names_the_key_among_the_credentials() {
        assert!(USAGE.contains("ApiKey"));
    }

    #[tokio::test]
    async fn a_call_to_the_shutdown_route_stops_the_service() {
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        let state = Arc::new(AppState::serving(test_cfg("http://127.0.0.1:1".into())));
        let serving = tokio::spawn(serve_until_stopped(listener, state));

        let answer = reqwest::Client::new().post(format!("http://{addr}/shutdown")).send().await.unwrap();

        assert!(answer.status().is_success());
        tokio::time::timeout(std::time::Duration::from_secs(5), serving)
            .await
            .expect("the service was still serving five seconds after it answered /shutdown")
            .unwrap();
    }

    // ---- A platform presenting a certificate made for the test ----

    async fn untrusted_platform() -> (String, Arc<AtomicU64>) {
        let (url, requests, _) = platform_with_a_certificate_of_its_own().await;
        (url, requests)
    }

    /// An HTTPS server presenting a certificate made for this test alone, which counts every request
    /// that gets past the handshake. The certificate comes back so that a test can have it trusted.
    async fn platform_with_a_certificate_of_its_own() -> (String, Arc<AtomicU64>, String) {
        let certified = rcgen::generate_simple_self_signed(vec!["localhost".to_string()]).unwrap();
        let certificate = certified.cert.pem();
        let chain = vec![certified.cert.der().clone()];
        let key = tokio_rustls::rustls::pki_types::PrivateKeyDer::Pkcs8(certified.key_pair.serialize_der().into());
        let tls = tokio_rustls::rustls::ServerConfig::builder_with_provider(Arc::new(
            tokio_rustls::rustls::crypto::ring::default_provider()))
            .with_safe_default_protocol_versions().unwrap()
            .with_no_client_auth()
            .with_single_cert(chain, key).unwrap();
        let acceptor = tokio_rustls::TlsAcceptor::from(Arc::new(tls));
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let requests = Arc::new(AtomicU64::new(0));
        let counted = requests.clone();
        tokio::spawn(async move {
            loop {
                let Ok((stream, _)) = listener.accept().await else { return };
                let (acceptor, counted) = (acceptor.clone(), counted.clone());
                tokio::spawn(async move {
                    use tokio::io::{AsyncReadExt, AsyncWriteExt};
                    let Ok(mut stream) = acceptor.accept(stream).await else { return };
                    let mut request = [0u8; 4096];
                    if matches!(stream.read(&mut request).await, Ok(read) if read > 0) {
                        counted.fetch_add(1, Ordering::SeqCst);
                        let _ = stream.write_all(b"HTTP/1.1 200 OK\r\ncontent-length: 17\r\n\r\n{\"token\":\"never\"}").await;
                    }
                });
            }
        });
        (format!("https://localhost:{port}"), requests, certificate)
    }

    const PLATFORM_UNDER_TEST: &str = "VOS_RUST_ECHO_PLATFORM_UNDER_TEST";

    // What the machine trusts cannot be changed from inside a test, so the trusted case runs in a
    // process of its own, which SSL_CERT_FILE tells to trust the certificate the server presents.
    #[tokio::test]
    async fn registration_reaches_a_platform_whose_certificate_the_machine_trusts() {
        let (url, requests, certificate) = platform_with_a_certificate_of_its_own().await;
        let trusted = std::env::temp_dir().join(format!("vos-rust-echo-trusted-{}.pem", uuid::Uuid::new_v4()));
        std::fs::write(&trusted, certificate).unwrap();

        let registering = tokio::process::Command::new(std::env::current_exe().unwrap())
            .args(["--exact", "tests::registers_with_the_platform_named_in_the_environment", "--ignored"])
            .env("SSL_CERT_FILE", &trusted)
            .env(PLATFORM_UNDER_TEST, &url)
            .output()
            .await
            .unwrap();
        std::fs::remove_file(&trusted).unwrap();

        assert!(registering.status.success(), "{}", String::from_utf8_lossy(&registering.stdout));
        assert_eq!(requests.load(Ordering::SeqCst), 1, "registration never reached the platform");
    }

    #[tokio::test]
    #[ignore = "run by registration_reaches_a_platform_whose_certificate_the_machine_trusts, in a process of its own"]
    async fn registers_with_the_platform_named_in_the_environment() {
        let Ok(url) = std::env::var(PLATFORM_UNDER_TEST) else { return };

        let said = registration(&AppState::serving(test_cfg(url))).await;

        assert!(said.starts_with("registered with mycelium"), "{said}");
    }

    #[tokio::test]
    async fn a_key_is_not_sent_to_a_platform_whose_certificate_the_machine_does_not_trust() {
        let (url, requests) = untrusted_platform().await;
        let cfg = Config { api_key: Some("vos_ak_example".into()), token: None, ..test_cfg(url) };

        let refusal = token_at(&cfg, &platform_client().unwrap(), A_MOMENT).await.unwrap_err();

        assert_eq!(requests.load(Ordering::SeqCst), 0, "the key reached a server whose certificate nothing vouches for");
        assert!(refusal.contains("certificate"), "the refusal does not say the certificate was refused: {refusal}");
    }

    #[tokio::test]
    async fn registration_does_not_send_a_token_to_a_platform_whose_certificate_the_machine_does_not_trust() {
        let (url, requests) = untrusted_platform().await;

        let said = registration(&AppState::serving(test_cfg(url))).await;

        assert_eq!(requests.load(Ordering::SeqCst), 0, "the token reached a server whose certificate nothing vouches for");
        assert!(said.contains("certificate"), "{said}");
    }

    // Each demo route reaches the platform on its own, so each is held to checking the certificate.
    #[tokio::test]
    async fn each_demo_route_answers_a_refused_certificate_and_sends_nothing() {
        let (url, requests) = untrusted_platform().await;
        let state = Arc::new(AppState::serving(test_cfg(url)));
        let thing: DemoReq = serde_json::from_value(json!({ "thingId": "t1" })).unwrap();

        for answer in [
            demo_write_kinds(State(state.clone()), Some(Json(thing))).await,
            demo_subscribe(State(state.clone()), None).await,
        ] {
            let (status, said) = answered(answer).await;
            assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
            assert!(said["error"].as_str().unwrap().contains("certificate"), "{said}");
        }
        assert_eq!(requests.load(Ordering::SeqCst), 0, "the token reached a server whose certificate nothing vouches for");
    }
}
