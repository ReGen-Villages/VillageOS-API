package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
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

func TestParseArgs(t *testing.T) {
	cfg, err := parseArgs([]string{"--port=5101", "--myceliumUrl=https://localhost:7243/"})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cfg.Port != 5101 || cfg.MyceliumURL != "https://localhost:7243" {
		t.Fatalf("bad parse: %+v", cfg)
	}
	if cfg.Issuer != "VillageOS" || cfg.Audience != "VosClients" {
		t.Fatalf("defaults not applied: %+v", cfg)
	}
	if _, err := parseArgs([]string{"--port=5101"}); err == nil {
		t.Fatal("expected error when --myceliumUrl missing")
	}
	if _, err := parseArgs([]string{"--port=0", "--myceliumUrl=x"}); err == nil {
		t.Fatal("expected error for out-of-range port")
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
