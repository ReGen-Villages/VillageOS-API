//! VillageOS managed-microservice example — Rust + Axum.
//!
//! A managed microservice is a handler that Mycelium (the VillageOS gateway)
//! launches as a daemon and calls when a relationship with the service's
//! predicate is created. The whole contract is HTTP + a single HS256 JWT.
//!
//! `is` is NOT an external predicate — Mycelium handles `is` inheritance
//! in-process and never dispatches it. Register for a custom predicate instead.
//!
//! Run: cargo run -- --port=5104 --myceliumUrl=https://localhost:7243

use std::sync::{
    atomic::{AtomicU64, Ordering},
    Arc,
};

use axum::{
    extract::{Request, State},
    http::StatusCode,
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::{get, post},
    Json, Router,
};
use base64::{engine::general_purpose::STANDARD as B64, Engine};
use jsonwebtoken::{decode, Algorithm, DecodingKey, Validation};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

const SERVICE_NAME: &str = "Rust";

#[derive(Clone)]
struct Config {
    port: u16,
    mycelium_url: String,
    token: Option<String>,
    signing_key: Option<String>, // base64-encoded HMAC key
    #[allow(dead_code)]
    issuer: String,
    audience: String,
}

/// Parses the standard --key=value flags. Returns None if required ones missing.
fn parse_args(args: &[String]) -> Option<Config> {
    let mut port: Option<u16> = None;
    let mut mycelium_url: Option<String> = None;
    let mut token = None;
    let mut signing_key = None;
    let mut issuer = "VillageOS".to_string();
    let mut audience = "VosClients".to_string();

    for a in args {
        let Some((k, v)) = a.split_once('=') else { continue };
        match k {
            "--port" => port = v.parse::<u16>().ok().filter(|p| *p >= 1),
            "--myceliumUrl" => mycelium_url = Some(v.trim_end_matches('/').to_string()),
            "--token" => token = Some(v.to_string()),
            "--signingKey" => signing_key = Some(v.to_string()),
            "--issuer" if !v.is_empty() => issuer = v.to_string(),
            "--audience" if !v.is_empty() => audience = v.to_string(),
            _ => {}
        }
    }

    Some(Config {
        port: port?,
        mycelium_url: mycelium_url?,
        token,
        signing_key,
        issuer,
        audience,
    })
}

const USAGE: &str = "Usage: app --port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]";

struct AppState {
    config: Config,
    handler_id: String,
    requests: AtomicU64,
}

// ---- inbound JWT validation (HS256) --------------------------------------

#[derive(Debug, Deserialize)]
#[allow(dead_code)]
struct Claims {
    iss: String,
    aud: String,
    exp: usize,
}

/// Validates an HS256 JWT against the base64-encoded signing key, issuer and
/// audience, allowing 30s clock skew — matching ServiceTokenValidator (.NET).
fn verify_jwt(token: &str, base64_key: &str, issuer: &str, audience: &str) -> bool {
    let Ok(key_bytes) = B64.decode(base64_key) else {
        return false;
    };
    let mut validation = Validation::new(Algorithm::HS256);
    validation.set_issuer(&[issuer]);
    validation.set_audience(&[audience]);
    validation.leeway = 30;
    decode::<Claims>(token, &DecodingKey::from_secret(&key_bytes), &validation).is_ok()
}

/// Axum middleware: when a signing key is configured, require a valid
/// Mycelium-signed Bearer JWT. No-op otherwise (matches the .NET handlers).
async fn auth(State(state): State<Arc<AppState>>, req: Request, next: Next) -> Response {
    let Some(key) = state.config.signing_key.as_deref() else {
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

// ---- endpoints -----------------------------------------------------------

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

async fn shutdown() -> Json<Value> {
    Json(json!({ "message": format!("Shutting down {SERVICE_NAME} microservice") }))
}

// ---- Mycelium registration ----------------------------------------------

async fn get_token(cfg: &Config, http: &reqwest::Client) -> reqwest::Result<String> {
    if let Some(t) = &cfg.token {
        return Ok(t.clone());
    }
    let resp = http
        .post(format!("{}/api/auth/token", cfg.mycelium_url))
        .send()
        .await?
        .error_for_status()?;
    let body: Value = resp.json().await?;
    Ok(body.get("token").and_then(Value::as_str).unwrap_or_default().to_string())
}

async fn register(state: &AppState, http: &reqwest::Client) -> reqwest::Result<()> {
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
        .await?
        .error_for_status()?;
    Ok(())
}

async fn deregister(state: &AppState, http: &reqwest::Client) {
    let Ok(token) = get_token(&state.config, http).await else {
        return;
    };
    let _ = http
        .delete(format!(
            "{}/api/mycelium/services/{}",
            state.config.mycelium_url, state.handler_id
        ))
        .bearer_auth(token)
        .send()
        .await;
}

// ---- Write kinds: Facts · Observations · Sediment ------------------------
//
// Three ways a microservice writes back to the model, over reqwest so the wire
// contract is explicit. See docs/MICROSERVICE_CONTRACT.md § "Writing data back".

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
    thing_id: String,
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

/// Assert a structural Fact (synchronous, never lossy). Returns the commit sequence number.
/// 405 means the property is ObservationOnly; 404 means the thing/property is unknown.
async fn set_fact(cfg: &Config, http: &reqwest::Client, thing_id: &str, property: &str, value: Value) -> Result<i64, String> {
    let token = get_token(cfg, http).await.map_err(|e| e.to_string())?;
    let url = format!("{}/api/things/{}/properties/{}/facts", cfg.mycelium_url, thing_id, enc(property));
    let resp = http.post(url).bearer_auth(token).json(&json!({ "value": value })).send().await.map_err(|e| e.to_string())?;
    if resp.status().as_u16() != 201 {
        return Err(format!("fact write returned {}", resp.status()));
    }
    let body: Value = resp.json().await.map_err(|e| e.to_string())?;
    Ok(body.get("sequenceNumber").and_then(Value::as_i64).unwrap_or(0))
}

/// Record one sampled Observation (queued/batched; 202). Pass observed_at (ISO-8601) for late or
/// out-of-order samples, or None to let Mycelium stamp now. 405 if the property is FactOnly.
async fn record_observation(cfg: &Config, http: &reqwest::Client, thing_id: &str, property: &str, value: Value, observed_at: Option<&str>) -> Result<(), String> {
    let token = get_token(cfg, http).await.map_err(|e| e.to_string())?;
    let mut body = json!({ "value": value });
    if let Some(at) = observed_at {
        body["observedAt"] = json!(at);
    }
    let url = format!("{}/api/things/{}/properties/{}/observations", cfg.mycelium_url, thing_id, enc(property));
    let resp = http.post(url).bearer_auth(token).json(&body).send().await.map_err(|e| e.to_string())?;
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
    let token = get_token(cfg, http).await.map_err(|e| e.to_string())?;
    let url = format!("{}/api/things/{}/observations", cfg.mycelium_url, thing_id);
    let resp = http.post(url).bearer_auth(token).json(&samples).send().await.map_err(|e| e.to_string())?;
    if !resp.status().is_success() {
        return Err(format!("observation batch returned {}", resp.status()));
    }
    let body: Value = resp.json().await.map_err(|e| e.to_string())?;
    Ok(body.get("accepted").and_then(Value::as_i64).unwrap_or(samples.len() as i64))
}

/// Bulk-load historical readings straight to sealed Sapwood (202). Entities must already exist and
/// every reading must carry observed_at. Returns the deposit summary.
async fn deposit_sediment(cfg: &Config, http: &reqwest::Client, readings: &[SedimentReading]) -> Result<SedimentResult, String> {
    if readings.is_empty() {
        return Err("at least one reading is required".to_string());
    }
    let token = get_token(cfg, http).await.map_err(|e| e.to_string())?;
    let url = format!("{}/api/sediment", cfg.mycelium_url);
    let resp = http.post(url).bearer_auth(token).json(&readings).send().await.map_err(|e| e.to_string())?;
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

/// Runnable worked example: POST {"thingId":"..."} drives one Fact, one single + one batch
/// Observation, and one Sediment deposit against an already-existing Thing. Timestamps are
/// illustrative fixed values to keep the example dependency-free.
async fn demo_write_kinds(State(state): State<Arc<AppState>>, body: Option<Json<DemoReq>>) -> Response {
    let thing_id = match body.and_then(|Json(b)| b.thing_id) {
        Some(t) if !t.is_empty() => t,
        _ => return (StatusCode::BAD_REQUEST, Json(json!({ "error": "thingId is required" }))).into_response(),
    };
    let http = match reqwest::Client::builder().danger_accept_invalid_certs(true).build() {
        Ok(c) => c,
        Err(e) => return (StatusCode::INTERNAL_SERVER_ERROR, Json(json!({ "error": e.to_string() }))).into_response(),
    };
    let cfg = &state.config;

    let run = async {
        let seq = set_fact(cfg, &http, &thing_id, "status", json!("active")).await?;
        record_observation(cfg, &http, &thing_id, "temperature", json!(21.5), Some("2026-06-20T12:00:00Z")).await?;
        let accepted = record_observations(cfg, &http, &thing_id, &[
            ObservationSample { property: "temperature".into(), value: json!(21.7), observed_at: None },
            ObservationSample { property: "flow".into(), value: json!(3.1), observed_at: None },
        ]).await?;
        let deposit = deposit_sediment(cfg, &http, &[
            SedimentReading { thing_id: thing_id.clone(), property: "temperature".into(), value: json!(19.8), observed_at: "2026-06-19T12:00:00Z".into() },
            SedimentReading { thing_id: thing_id.clone(), property: "temperature".into(), value: json!(20.4), observed_at: "2026-06-19T13:00:00Z".into() },
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

// ---- Snapshot selector: subscribe to a slice of the model ----------------
//
// The selector replaced launch-time object IDs (the retired ServiceArgs ID
// template). See docs/MICROSERVICE_CONTRACT.md § "Selecting a slice".

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

#[derive(Deserialize)]
struct SnapThing {
    id: String,
    name: Option<String>,
}

#[derive(Deserialize)]
struct SnapRel {
    #[allow(dead_code)]
    id: String,
}

#[derive(Deserialize)]
struct Snapshot {
    things: Vec<SnapThing>,
    relationships: Vec<SnapRel>,
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
    let token = get_token(cfg, http).await.map_err(|e| e.to_string())?;
    let url = format!("{}/api/subscriptions", cfg.mycelium_url);
    let resp = http.post(url).bearer_auth(token).json(selector).send().await.map_err(|e| e.to_string())?;
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

/// POST {"type":"...","predicate":"..."} (defaults to Battery/powers); subscribes for that slice,
/// reports the resolved closure, and unsubscribes.
async fn demo_subscribe(State(state): State<Arc<AppState>>, body: Option<Json<SubDemoReq>>) -> Response {
    let (type_, predicate) = body.map(|Json(b)| (b.type_, b.predicate)).unwrap_or((None, None));
    let type_ = type_.unwrap_or_else(|| "Battery".into());
    let predicate = predicate.unwrap_or_else(|| "powers".into());
    let http = match reqwest::Client::builder().danger_accept_invalid_certs(true).build() {
        Ok(c) => c,
        Err(e) => return (StatusCode::INTERNAL_SERVER_ERROR, Json(json!({ "error": e.to_string() }))).into_response(),
    };
    let cfg = &state.config;
    match subscribe(cfg, &http, &slice_by_type_and_traverse(&type_, &predicate)).await {
        Ok(sub) => {
            let names: Vec<String> = sub
                .snapshot
                .things
                .iter()
                .map(|t| t.name.clone().unwrap_or_else(|| t.id.clone()))
                .collect();
            unsubscribe(cfg, &http, &sub.subscription_id).await;
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
    let Some(config) = parse_args(&args) else {
        eprintln!("{USAGE}");
        std::process::exit(1);
    };

    let port = config.port;
    let auth_enabled = config.signing_key.is_some();
    let state = Arc::new(AppState {
        config,
        handler_id: uuid::Uuid::new_v4().to_string(),
        requests: AtomicU64::new(0),
    });
    println!(
        "VillageOS {SERVICE_NAME} microservice — port {port}, mycelium {}, auth={auth_enabled}",
        state.config.mycelium_url
    );

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

    // Register once the listener is bound.
    let reg_state = state.clone();
    tokio::spawn(async move {
        tokio::time::sleep(std::time::Duration::from_millis(200)).await;
        let http = reqwest::Client::builder()
            .danger_accept_invalid_certs(true) // dev: Mycelium uses a self-signed cert
            .build()
            .expect("http client");
        match register(&reg_state, &http).await {
            Ok(()) => println!("registered with mycelium as {}", reg_state.handler_id),
            Err(e) => eprintln!("registration failed: {e}"),
        }
    });

    let listener = tokio::net::TcpListener::bind(("127.0.0.1", port))
        .await
        .expect("bind");

    // Graceful shutdown on Ctrl-C: deregister, then stop.
    let shut_state = state.clone();
    axum::serve(listener, app)
        .with_graceful_shutdown(async move {
            let _ = tokio::signal::ctrl_c().await;
            let http = reqwest::Client::builder()
                .danger_accept_invalid_certs(true)
                .build()
                .expect("http client");
            println!(
                "shutting down — processed {} request(s)",
                shut_state.requests.load(Ordering::SeqCst)
            );
            deregister(&shut_state, &http).await;
        })
        .await
        .expect("server");
}

#[cfg(test)]
mod tests {
    use super::*;
    use jsonwebtoken::{encode, EncodingKey, Header};
    use serde::Serialize;

    const KEY_RAW: &[u8] = b"vos-test-signing-key-0123456789ab";

    fn b64_key() -> String {
        B64.encode(KEY_RAW)
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
        encode(&Header::new(Algorithm::HS256), &claims, &EncodingKey::from_secret(KEY_RAW)).unwrap()
    }

    #[test]
    fn parse_args_valid_with_defaults() {
        let cfg = parse_args(&[
            "--port=5104".into(),
            "--myceliumUrl=https://localhost:7243/".into(),
        ])
        .unwrap();
        assert_eq!(cfg.port, 5104);
        assert_eq!(cfg.mycelium_url, "https://localhost:7243");
        assert_eq!(cfg.issuer, "VillageOS");
        assert_eq!(cfg.audience, "VosClients");
    }

    #[test]
    fn parse_args_missing_required_returns_none() {
        assert!(parse_args(&["--port=5104".into()]).is_none());
        assert!(parse_args(&["--myceliumUrl=x".into()]).is_none());
    }

    #[test]
    fn verify_jwt_accepts_valid() {
        let tok = make_token("VillageOS", "VosClients", 60);
        assert!(verify_jwt(&tok, &b64_key(), "VillageOS", "VosClients"));
    }

    #[test]
    fn verify_jwt_rejects_tampered() {
        let tok = make_token("VillageOS", "VosClients", 60) + "x";
        assert!(!verify_jwt(&tok, &b64_key(), "VillageOS", "VosClients"));
    }

    #[test]
    fn verify_jwt_rejects_expired() {
        let tok = make_token("VillageOS", "VosClients", -120);
        assert!(!verify_jwt(&tok, &b64_key(), "VillageOS", "VosClients"));
    }

    #[test]
    fn verify_jwt_rejects_wrong_issuer_and_audience() {
        let bad_iss = make_token("Attacker", "VosClients", 60);
        let bad_aud = make_token("VillageOS", "Nope", 60);
        assert!(!verify_jwt(&bad_iss, &b64_key(), "VillageOS", "VosClients"));
        assert!(!verify_jwt(&bad_aud, &b64_key(), "VillageOS", "VosClients"));
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
            token: Some("tok".into()),
            signing_key: None,
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
            SedimentReading { thing_id: "t1".into(), property: "flow".into(), value: json!(1.0), observed_at: "2026-06-19T00:00:00Z".into() },
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

    // ---- Snapshot selector ---- (reuses Mutex/AUTHORIZATION imports above)
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
                    "things": [{ "id": "t1", "name": "Battery-1" }, { "id": "t2", "name": "Inverter-7" }],
                    "relationships": [{ "id": "r1" }]
                }
            }))).into_response()
        } else {
            StatusCode::OK.into_response()
        }
    }

    fn sel_cfg(url: String) -> Config {
        Config {
            port: 0,
            mycelium_url: url,
            token: Some("tok".into()),
            signing_key: None,
            issuer: "VillageOS".into(),
            audience: "VosClients".into(),
        }
    }

    #[test]
    fn slice_by_type_and_traverse_builds_selector() {
        let body = serde_json::to_string(&slice_by_type_and_traverse("Battery", "powers")).unwrap();
        assert!(body.contains("\"types\"") && body.contains("Battery"));
        assert!(body.contains("\"traverse\"") && body.contains("powers"));
        assert!(!body.contains("\"all\"")); // unset fields omitted
    }

    #[tokio::test]
    async fn subscribe_posts_selector_and_returns_closure() {
        let cap: Cap = Arc::new(Mutex::new(Vec::new()));
        let app = Router::new().fallback(sub_mock).with_state(cap.clone());
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let addr = listener.local_addr().unwrap();
        tokio::spawn(async move { axum::serve(listener, app).await.unwrap() });

        let cfg = sel_cfg(format!("http://{addr}"));
        let http = reqwest::Client::new();
        let sub = subscribe(&cfg, &http, &slice_by_type_and_traverse("Battery", "powers")).await.unwrap();
        assert_eq!(sub.subscription_id, "s-1");
        assert_eq!(sub.snapshot.things.len(), 2);
        assert_eq!(sub.snapshot.relationships.len(), 1);
        unsubscribe(&cfg, &http, &sub.subscription_id).await;

        let calls = cap.lock().unwrap();
        assert_eq!(calls[0].0, "/api/subscriptions");
        assert_eq!(calls[0].1, "Bearer tok");
        assert!(calls[0].2.contains("types") && calls[0].2.contains("Battery") && calls[0].2.contains("powers"));
        assert_eq!(calls[1].0, "/api/subscriptions/s-1");
    }
}
