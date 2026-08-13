package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

// makeToken builds an HS256 JWT signed with key for use in auth tests.
func makeToken(key []byte, claims map[string]any) string {
	enc := func(v any) string {
		b, _ := json.Marshal(v)
		return base64.RawURLEncoding.EncodeToString(b)
	}
	head := enc(map[string]string{"alg": "HS256", "typ": "JWT"})
	pay := enc(claims)
	mac := hmac.New(sha256.New, key)
	mac.Write([]byte(head + "." + pay))
	sig := base64.RawURLEncoding.EncodeToString(mac.Sum(nil))
	return head + "." + pay + "." + sig
}

func validClaims() map[string]any {
	now := time.Now().Unix()
	return map[string]any{
		"iss": "VillageOS", "aud": "VosClients", "sub": "mycelium",
		"vos:token_type": "mycelium_request",
		"iat":            now, "nbf": now, "exp": now + 60,
	}
}

func environmentOf(values map[string]string) func(string) string {
	return func(name string) string { return values[name] }
}

var emptyEnvironment = environmentOf(nil)

func TestParseArgs(t *testing.T) {
	cfg, err := parseArgs([]string{"--port=5101", "--myceliumUrl=https://localhost:7243/"}, emptyEnvironment)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cfg.Port != 5101 || cfg.MyceliumURL != "https://localhost:7243" {
		t.Fatalf("bad parse: %+v", cfg)
	}
	if cfg.Issuer != "VillageOS" || cfg.Audience != "VosClients" {
		t.Fatalf("defaults not applied: %+v", cfg)
	}
	if _, err := parseArgs([]string{"--port=5101"}, emptyEnvironment); err == nil {
		t.Fatal("expected error when --myceliumUrl missing")
	}
	if _, err := parseArgs([]string{"--port=0", "--myceliumUrl=x"}, emptyEnvironment); err == nil {
		t.Fatal("expected error for out-of-range port")
	}
}

func TestParseArgsTakesCredentialsFromTheEnvironment(t *testing.T) {
	cfg, err := parseArgs(
		[]string{"--port=5101", "--myceliumUrl=https://localhost:7243"},
		environmentOf(map[string]string{"Token": "environment-token", "SigningKey": "ZW52aXJvbm1lbnQta2V5"}))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cfg.Token != "environment-token" {
		t.Fatalf("token = %q, want the environment value", cfg.Token)
	}
	if cfg.SigningKey != "ZW52aXJvbm1lbnQta2V5" {
		t.Fatalf("signing key = %q, want the environment value", cfg.SigningKey)
	}
}

func TestParseArgsIgnoresCredentialsGivenAsFlags(t *testing.T) {
	cfg, err := parseArgs(
		[]string{"--port=5101", "--myceliumUrl=https://localhost:7243", "--token=flag-token", "--signingKey=flag-key"},
		emptyEnvironment)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cfg.Token != "" || cfg.SigningKey != "" {
		t.Fatalf("a credential flag was accepted: %+v", cfg)
	}
}

func TestVerifyHS256(t *testing.T) {
	key := []byte("vos-test-signing-key-0123456789ab")
	cases := []struct {
		name   string
		claims map[string]any
		mutate func(string) string
		wantOK bool
	}{
		{"valid", validClaims(), nil, true},
		{"expired", func() map[string]any { c := validClaims(); c["exp"] = time.Now().Unix() - 120; return c }(), nil, false},
		{"wrong issuer", func() map[string]any { c := validClaims(); c["iss"] = "Attacker"; return c }(), nil, false},
		{"wrong audience", func() map[string]any { c := validClaims(); c["aud"] = "Nope"; return c }(), nil, false},
		{"tampered", validClaims(), func(tok string) string { return tok + "x" }, false},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			tok := makeToken(key, tc.claims)
			if tc.mutate != nil {
				tok = tc.mutate(tok)
			}
			err := verifyHS256(tok, key, "VillageOS", "VosClients")
			if tc.wantOK && err != nil {
				t.Fatalf("expected valid, got %v", err)
			}
			if !tc.wantOK && err == nil {
				t.Fatal("expected invalid, got nil error")
			}
		})
	}
}

func TestVerifyHS256_WrongKey(t *testing.T) {
	tok := makeToken([]byte("the-real-key"), validClaims())
	if err := verifyHS256(tok, []byte("a-different-key"), "VillageOS", "VosClients"); err == nil {
		t.Fatal("expected signature mismatch with wrong key")
	}
}

func TestHandleEcho(t *testing.T) {
	s := &service{cfg: config{Issuer: "VillageOS", Audience: "VosClients"}, handlerID: "test-id"}
	body := `{"relationshipId":"r1","subjectName":"A","properties":{"k":1}}`
	req := httptest.NewRequest(http.MethodPost, "/handle", strings.NewReader(body))
	rec := httptest.NewRecorder()

	s.handleRelationship(rec, req)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d", rec.Code)
	}
	var resp map[string]any
	if err := json.Unmarshal(rec.Body.Bytes(), &resp); err != nil {
		t.Fatal(err)
	}
	if resp["success"] != true || resp["relationshipId"] != "r1" {
		t.Fatalf("unexpected response: %v", resp)
	}
	if s.requests.Load() != 1 {
		t.Fatalf("request counter = %d", s.requests.Load())
	}
}

func TestRequireAuth(t *testing.T) {
	key := []byte("vos-test-signing-key-0123456789ab")
	s := &service{cfg: config{
		SigningKey: base64.StdEncoding.EncodeToString(key),
		Issuer:     "VillageOS", Audience: "VosClients",
	}}
	guarded := s.requireAuth(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusOK)
	})

	// no token -> 401
	rec := httptest.NewRecorder()
	guarded(rec, httptest.NewRequest(http.MethodPost, "/handle", nil))
	if rec.Code != http.StatusUnauthorized {
		t.Fatalf("missing token: status = %d", rec.Code)
	}

	// valid token -> 200
	rec = httptest.NewRecorder()
	req := httptest.NewRequest(http.MethodPost, "/handle", nil)
	req.Header.Set("Authorization", "Bearer "+makeToken(key, validClaims()))
	guarded(rec, req)
	if rec.Code != http.StatusOK {
		t.Fatalf("valid token: status = %d", rec.Code)
	}
}

func TestHealth(t *testing.T) {
	s := &service{cfg: config{}}
	rec := httptest.NewRecorder()
	s.health(rec, httptest.NewRequest(http.MethodGet, "/health", nil))
	var resp map[string]any
	_ = json.Unmarshal(rec.Body.Bytes(), &resp)
	if resp["status"] != "Healthy" {
		t.Fatalf("health: %v", resp)
	}
}

// captured records one inbound request to the mock Mycelium.
type capturedReq struct{ method, path, auth, body string }

func mockMycelium(t *testing.T, got *[]capturedReq) *httptest.Server {
	t.Helper()
	return httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		*got = append(*got, capturedReq{r.Method, r.URL.Path, r.Header.Get("Authorization"), string(b)})
		switch {
		case strings.HasSuffix(r.URL.Path, "/facts"):
			w.WriteHeader(http.StatusCreated)
			_, _ = w.Write([]byte(`{"sequenceNumber":42,"value":"active"}`))
		case strings.Contains(r.URL.Path, "/properties/") && strings.HasSuffix(r.URL.Path, "/observations"):
			w.WriteHeader(http.StatusAccepted)
		case strings.HasSuffix(r.URL.Path, "/observations"):
			w.WriteHeader(http.StatusAccepted)
			_, _ = w.Write([]byte(`{"accepted":2}`))
		case r.URL.Path == "/api/sediment":
			w.WriteHeader(http.StatusAccepted)
			_, _ = w.Write([]byte(`{"batchId":"b-1","series":1,"buckets":3,"samples":10}`))
		default:
			w.WriteHeader(http.StatusNotFound)
		}
	}))
}

func TestWriteKinds(t *testing.T) {
	var got []capturedReq
	srv := mockMycelium(t, &got)
	defer srv.Close()
	s := &service{cfg: config{MyceliumURL: srv.URL, Token: "tok"}, handlerID: "id", client: srv.Client()}

	seq, err := s.setFact("t1", "status", "active")
	if err != nil || seq != 42 {
		t.Fatalf("setFact: %d %v", seq, err)
	}
	if err := s.recordObservation("t1", "temperature", 21.5, "2026-06-20T14:00:00Z"); err != nil {
		t.Fatalf("recordObservation: %v", err)
	}
	n, err := s.recordObservations("t1", []observationSample{{Property: "temperature", Value: 21.7}, {Property: "flow", Value: 3.1}})
	if err != nil || n != 2 {
		t.Fatalf("recordObservations: %d %v", n, err)
	}
	res, err := s.depositSediment([]sedimentReading{{ThingID: "t1", Property: "flow", Value: 1.0, ObservedAt: "2026-06-19T00:00:00Z"}})
	if err != nil || res.BatchID != "b-1" || res.Samples != 10 {
		t.Fatalf("depositSediment: %+v %v", res, err)
	}

	wantPaths := []string{
		"/api/things/t1/properties/status/facts",
		"/api/things/t1/properties/temperature/observations",
		"/api/things/t1/observations",
		"/api/sediment",
	}
	if len(got) != 4 {
		t.Fatalf("expected 4 requests, got %d", len(got))
	}
	for i, want := range wantPaths {
		if got[i].path != want {
			t.Fatalf("req %d path = %s, want %s", i, got[i].path, want)
		}
		if got[i].auth != "Bearer tok" {
			t.Fatalf("req %d auth = %q", i, got[i].auth)
		}
	}
	if !strings.HasPrefix(strings.TrimSpace(got[2].body), "[") {
		t.Fatalf("batch body is not a JSON array: %s", got[2].body)
	}
	if !strings.Contains(got[3].body, "observedAt") {
		t.Fatalf("sediment body missing observedAt: %s", got[3].body)
	}
}

func TestWriteKinds_Errors(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusMethodNotAllowed) // e.g. wrong write kind for the property
	}))
	defer srv.Close()
	s := &service{cfg: config{MyceliumURL: srv.URL, Token: "tok"}, client: srv.Client()}

	if _, err := s.setFact("t", "p", "v"); err == nil {
		t.Fatal("expected error when fact write returns 405")
	}
	if err := s.recordObservation("t", "p", "v", ""); err == nil {
		t.Fatal("expected error when observation returns 405")
	}
	if _, err := s.depositSediment(nil); err == nil {
		t.Fatal("expected error for empty sediment batch")
	}
}

func TestSelectorSubscribe(t *testing.T) {
	type cap struct{ method, path, auth, body string }
	var got cap
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		if r.URL.Path == "/api/subscriptions" && r.Method == http.MethodPost {
			got = cap{r.Method, r.URL.Path, r.Header.Get("Authorization"), string(b)}
			w.WriteHeader(http.StatusOK)
			_, _ = w.Write([]byte(`{"subscriptionId":"s-1","watermark":42,"snapshot":{` +
				`"things":[{"id":"t1","name":"Battery-1"},{"id":"t2","name":"Inverter-7"}],` +
				`"relationships":[{"id":"r1"}]}}`))
			return
		}
		w.WriteHeader(http.StatusOK) // DELETE unsubscribe
	}))
	defer srv.Close()
	s := &service{cfg: config{MyceliumURL: srv.URL, Token: "tok"}, client: srv.Client()}

	sub, err := s.subscribe(sliceByTypeAndTraverse("Battery", "powers"))
	if err != nil {
		t.Fatalf("subscribe: %v", err)
	}
	if sub.SubscriptionID != "s-1" || sub.Watermark != 42 {
		t.Fatalf("bad result: %+v", sub)
	}
	if len(sub.Snapshot.Things) != 2 || len(sub.Snapshot.Relationships) != 1 {
		t.Fatalf("closure: %+v", sub.Snapshot)
	}
	if got.auth != "Bearer tok" {
		t.Fatalf("auth: %q", got.auth)
	}
	if !strings.Contains(got.body, `"types"`) || !strings.Contains(got.body, "Battery") {
		t.Fatalf("selector body missing types: %s", got.body)
	}
	if !strings.Contains(got.body, "powers") {
		t.Fatalf("selector body missing traverse: %s", got.body)
	}
	s.unsubscribe(sub.SubscriptionID)
}
