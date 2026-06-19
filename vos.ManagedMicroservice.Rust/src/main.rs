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
use serde::Deserialize;
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
}
