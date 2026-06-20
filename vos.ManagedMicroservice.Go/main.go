// VillageOS managed-microservice example — Go (standard library only).
//
// A managed microservice is a handler that Mycelium (the VillageOS gateway)
// launches as a daemon and calls when a relationship with the service's
// predicate is created. The full contract is HTTP + a single HS256 JWT.
//
// Lifecycle:
//  1. Mycelium launches:  ./app --port=5101 --myceliumUrl=https://localhost:7243 \
//     [--token=<jwt>] [--signingKey=<base64>] \
//     [--issuer=VillageOS] [--audience=VosClients]
//  2. On startup the service registers (POST /api/mycelium/register).
//  3. Mycelium calls POST /handle for each matching relationship (JWT-authed).
//  4. On shutdown (SIGINT/SIGTERM or POST /shutdown) it deregisters
//     (DELETE /api/mycelium/services/{handlerId}).
package main

import (
	"context"
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"strconv"
	"strings"
	"sync/atomic"
	"syscall"
	"time"
)

const serviceName = "Go"

type config struct {
	Port        int
	MyceliumURL string
	Token       string // pre-minted service JWT; else fetched from Mycelium
	SigningKey  string // base64-encoded HMAC key for validating inbound JWTs
	Issuer      string
	Audience    string
}

func parseArgs(args []string) (config, error) {
	c := config{Issuer: "VillageOS", Audience: "VosClients"}
	var portSet, urlSet bool
	for _, a := range args {
		k, v, ok := strings.Cut(a, "=")
		if !ok {
			continue
		}
		switch k {
		case "--port":
			p, err := strconv.Atoi(v)
			if err != nil || p < 1 || p > 65535 {
				return c, fmt.Errorf("invalid --port %q", v)
			}
			c.Port, portSet = p, true
		case "--myceliumUrl":
			c.MyceliumURL, urlSet = strings.TrimRight(v, "/"), true
		case "--token":
			c.Token = v
		case "--signingKey":
			c.SigningKey = v
		case "--issuer":
			if v != "" {
				c.Issuer = v
			}
		case "--audience":
			if v != "" {
				c.Audience = v
			}
		}
	}
	if !portSet || !urlSet {
		return c, errors.New("missing required --port and/or --myceliumUrl")
	}
	return c, nil
}

const usage = `Usage: app --port=<port> --myceliumUrl=<url> [--token=<jwt>] [--signingKey=<base64>] [--issuer=<iss>] [--audience=<aud>]
  --port        Port to listen on (1-65535)
  --myceliumUrl Base URL of the VillageOS Mycelium gateway
  --token       Service JWT for authenticating to Mycelium (optional; else fetched)
  --signingKey  Base64 HMAC key for validating inbound /handle requests (optional)
  --issuer      JWT issuer Mycelium signs with (default VillageOS)
  --audience    JWT audience Mycelium signs with (default VosClients)`

type service struct {
	cfg       config
	handlerID string
	requests  atomic.Int64
	client    *http.Client
}

func (s *service) token() (string, error) {
	if s.cfg.Token != "" {
		return s.cfg.Token, nil
	}
	resp, err := s.client.Post(s.cfg.MyceliumURL+"/api/auth/token", "application/json", nil)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("token endpoint returned %d", resp.StatusCode)
	}
	var body struct {
		Token string `json:"token"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&body); err != nil {
		return "", err
	}
	return body.Token, nil
}

func (s *service) register() error {
	tok, err := s.token()
	if err != nil {
		return fmt.Errorf("get token: %w", err)
	}
	base := fmt.Sprintf("http://localhost:%d", s.cfg.Port)
	payload, _ := json.Marshal(map[string]string{
		"handlerId":      s.handlerID,
		"serviceName":    serviceName,
		"endpointUrl":    base,
		"startCommand":   "endpoint-service",
		"stopEndpoint":   base + "/shutdown",
		"healthEndpoint": base + "/health",
	})
	req, _ := http.NewRequest(http.MethodPost, s.cfg.MyceliumURL+"/api/mycelium/register", strings.NewReader(string(payload)))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+tok)
	resp, err := s.client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("register returned %d", resp.StatusCode)
	}
	return nil
}

func (s *service) deregister() {
	tok, err := s.token()
	if err != nil {
		log.Printf("deregister: could not get token: %v", err)
		return
	}
	req, _ := http.NewRequest(http.MethodDelete, s.cfg.MyceliumURL+"/api/mycelium/services/"+s.handlerID, nil)
	req.Header.Set("Authorization", "Bearer "+tok)
	resp, err := s.client.Do(req)
	if err != nil {
		log.Printf("deregister failed: %v", err)
		return
	}
	resp.Body.Close()
}

// ---- Write kinds: Facts · Observations · Sediment -------------------------------------------
//
// Three ways a microservice writes back to the model. Raw stdlib HTTP so the wire contract is
// explicit. See docs/MICROSERVICE_CONTRACT.md § "Writing data back".

type observationSample struct {
	Property   string `json:"property"`
	Value      any    `json:"value"`
	ObservedAt string `json:"observedAt,omitempty"` // ISO-8601; omit to let Mycelium stamp now
}

type sedimentReading struct {
	ThingID    string `json:"thingId"`
	Property   string `json:"property"`
	Value      any    `json:"value"`
	ObservedAt string `json:"observedAt"` // required — sediment is historical
}

type sedimentResult struct {
	BatchID string `json:"batchId"`
	Series  int    `json:"series"`
	Buckets int    `json:"buckets"`
	Samples int64  `json:"samples"`
}

// post issues an authenticated JSON POST to a Mycelium path.
func (s *service) post(path string, body any) (*http.Response, error) {
	tok, err := s.token()
	if err != nil {
		return nil, fmt.Errorf("get token: %w", err)
	}
	payload, err := json.Marshal(body)
	if err != nil {
		return nil, err
	}
	req, _ := http.NewRequest(http.MethodPost, s.cfg.MyceliumURL+path, strings.NewReader(string(payload)))
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", "Bearer "+tok)
	return s.client.Do(req)
}

// setFact asserts a structural Fact (synchronous, never lossy). Returns the commit sequence number.
// A 405 means the property is ObservationOnly; a 404 means the thing/property is unknown.
func (s *service) setFact(thingID, property string, value any) (int64, error) {
	resp, err := s.post(fmt.Sprintf("/api/things/%s/properties/%s/facts", thingID, url.PathEscape(property)),
		map[string]any{"value": value})
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusCreated {
		return 0, fmt.Errorf("fact write returned %d", resp.StatusCode)
	}
	var out struct {
		SequenceNumber int64 `json:"sequenceNumber"`
	}
	_ = json.NewDecoder(resp.Body).Decode(&out)
	return out.SequenceNumber, nil
}

// recordObservation records one sampled Observation (queued/batched; 202 Accepted). Pass observedAt
// (ISO-8601) for late/out-of-order samples, or "" to let Mycelium stamp now. 405 if FactOnly.
func (s *service) recordObservation(thingID, property string, value any, observedAt string) error {
	body := map[string]any{"value": value}
	if observedAt != "" {
		body["observedAt"] = observedAt
	}
	resp, err := s.post(fmt.Sprintf("/api/things/%s/properties/%s/observations", thingID, url.PathEscape(property)), body)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("observation write returned %d", resp.StatusCode)
	}
	return nil
}

// recordObservations records many samples across an entity's properties in one batch (202).
// Returns the accepted-sample count Mycelium reports.
func (s *service) recordObservations(thingID string, samples []observationSample) (int, error) {
	if len(samples) == 0 {
		return 0, nil
	}
	resp, err := s.post(fmt.Sprintf("/api/things/%s/observations", thingID), samples)
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return 0, fmt.Errorf("observation batch returned %d", resp.StatusCode)
	}
	var out struct {
		Accepted int `json:"accepted"`
	}
	_ = json.NewDecoder(resp.Body).Decode(&out)
	return out.Accepted, nil
}

// depositSediment bulk-loads historical readings straight to sealed Sapwood (202). Entities must
// already exist and every reading must carry observedAt. Returns the deposit summary.
func (s *service) depositSediment(readings []sedimentReading) (sedimentResult, error) {
	var res sedimentResult
	if len(readings) == 0 {
		return res, errors.New("at least one reading is required")
	}
	resp, err := s.post("/api/sediment", readings)
	if err != nil {
		return res, err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return res, fmt.Errorf("sediment deposit returned %d", resp.StatusCode)
	}
	_ = json.NewDecoder(resp.Body).Decode(&res)
	return res, nil
}

// demoWriteKinds is a runnable worked example: POST {"thingId":"..."} drives one Fact, one single
// Observation, one batch Observation, and one Sediment deposit against that (already-existing) Thing.
func (s *service) demoWriteKinds(w http.ResponseWriter, r *http.Request) {
	var req struct {
		ThingID string `json:"thingId"`
	}
	raw, _ := io.ReadAll(r.Body)
	_ = json.Unmarshal(raw, &req)
	if req.ThingID == "" {
		writeJSON(w, http.StatusBadRequest, map[string]any{"error": "thingId is required"})
		return
	}
	now := time.Now().UTC()
	fail := func(step string, err error) {
		writeJSON(w, http.StatusInternalServerError, map[string]any{"step": step, "error": err.Error()})
	}

	seq, err := s.setFact(req.ThingID, "status", "active")
	if err != nil {
		fail("fact", err)
		return
	}
	if err := s.recordObservation(req.ThingID, "temperature", 21.5, now.Format(time.RFC3339)); err != nil {
		fail("observation", err)
		return
	}
	accepted, err := s.recordObservations(req.ThingID, []observationSample{
		{Property: "temperature", Value: 21.7}, {Property: "flow", Value: 3.1},
	})
	if err != nil {
		fail("observation-batch", err)
		return
	}
	deposit, err := s.depositSediment([]sedimentReading{
		{ThingID: req.ThingID, Property: "temperature", Value: 19.8, ObservedAt: now.AddDate(0, 0, -1).Format(time.RFC3339)},
		{ThingID: req.ThingID, Property: "temperature", Value: 20.4, ObservedAt: now.AddDate(0, 0, -1).Add(time.Hour).Format(time.RFC3339)},
	})
	if err != nil {
		fail("sediment", err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"factSequence":         seq,
		"observationsAccepted": accepted + 1,
		"sedimentBatchId":      deposit.BatchID,
		"sedimentSamples":      deposit.Samples,
	})
}

// relationship mirrors the payload Mycelium POSTs to /handle.
type relationship struct {
	RelationshipID string         `json:"relationshipId"`
	SubjectID      string         `json:"subjectId"`
	TargetID       string         `json:"targetId"`
	SubjectName    string         `json:"subjectName"`
	TargetName     string         `json:"targetName"`
	ModelID        string         `json:"modelId"`
	Properties     map[string]any `json:"properties"`
}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}

func (s *service) handleRelationship(w http.ResponseWriter, r *http.Request) {
	n := s.requests.Add(1)
	raw, err := io.ReadAll(r.Body)
	if err != nil {
		writeJSON(w, http.StatusBadRequest, map[string]any{"success": false, "error": "unreadable body"})
		return
	}
	var rel relationship
	_ = json.Unmarshal(raw, &rel)
	log.Printf("handle #%d: relationship %s (%s -> %s)", n, rel.RelationshipID, rel.SubjectName, rel.TargetName)

	var echo any
	_ = json.Unmarshal(raw, &echo)
	writeJSON(w, http.StatusOK, map[string]any{
		"success":        true,
		"service":        serviceName,
		"requestNumber":  n,
		"relationshipId": rel.RelationshipID,
		"status":         "handled",
		"echo":           echo,
	})
}

func (s *service) health(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"status":            "Healthy",
		"service":           serviceName,
		"requestsProcessed": s.requests.Load(),
	})
}

func (s *service) stats(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"service":           serviceName,
		"version":           "1.0.0",
		"requestsProcessed": s.requests.Load(),
		"handlerId":         s.handlerID,
		"myceliumUrl":       s.cfg.MyceliumURL,
	})
}

// requireAuth requires a valid Bearer JWT only when a signing key was supplied;
// with no signing key, auth is disabled (matches the .NET handlers).
func (s *service) requireAuth(next http.HandlerFunc) http.HandlerFunc {
	if s.cfg.SigningKey == "" {
		return next
	}
	key, err := base64.StdEncoding.DecodeString(s.cfg.SigningKey)
	if err != nil {
		log.Fatalf("invalid --signingKey (not base64): %v", err)
	}
	return func(w http.ResponseWriter, r *http.Request) {
		auth := r.Header.Get("Authorization")
		tok, ok := strings.CutPrefix(auth, "Bearer ")
		if !ok {
			writeJSON(w, http.StatusUnauthorized, map[string]any{"error": "missing bearer token"})
			return
		}
		if err := verifyHS256(tok, key, s.cfg.Issuer, s.cfg.Audience); err != nil {
			writeJSON(w, http.StatusUnauthorized, map[string]any{"error": err.Error()})
			return
		}
		next(w, r)
	}
}

// verifyHS256 allows 30s of clock skew, matching ServiceTokenValidator on the .NET side.
func verifyHS256(token string, key []byte, issuer, audience string) error {
	parts := strings.Split(token, ".")
	if len(parts) != 3 {
		return errors.New("malformed token")
	}
	signing := parts[0] + "." + parts[1]
	mac := hmac.New(sha256.New, key)
	mac.Write([]byte(signing))
	expected := mac.Sum(nil)
	got, err := base64.RawURLEncoding.DecodeString(parts[2])
	if err != nil {
		return errors.New("bad signature encoding")
	}
	if subtle.ConstantTimeCompare(expected, got) != 1 {
		return errors.New("signature mismatch")
	}
	payloadJSON, err := base64.RawURLEncoding.DecodeString(parts[1])
	if err != nil {
		return errors.New("bad payload encoding")
	}
	var claims struct {
		Iss string          `json:"iss"`
		Aud json.RawMessage `json:"aud"`
		Exp int64           `json:"exp"`
		Nbf int64           `json:"nbf"`
	}
	if err := json.Unmarshal(payloadJSON, &claims); err != nil {
		return errors.New("bad payload json")
	}
	const skew = 30
	now := time.Now().Unix()
	if claims.Exp != 0 && now > claims.Exp+skew {
		return errors.New("token expired")
	}
	if claims.Nbf != 0 && now < claims.Nbf-skew {
		return errors.New("token not yet valid")
	}
	if claims.Iss != issuer {
		return errors.New("issuer mismatch")
	}
	if !audienceMatches(claims.Aud, audience) {
		return errors.New("audience mismatch")
	}
	return nil
}

// audienceMatches handles aud being either a string or an array of strings.
func audienceMatches(raw json.RawMessage, want string) bool {
	if len(raw) == 0 {
		return false
	}
	var one string
	if json.Unmarshal(raw, &one) == nil {
		return one == want
	}
	var many []string
	if json.Unmarshal(raw, &many) == nil {
		for _, a := range many {
			if a == want {
				return true
			}
		}
	}
	return false
}

func newUUID() string {
	var b [16]byte
	_, _ = rand.Read(b[:])
	b[6] = (b[6] & 0x0f) | 0x40 // version 4
	b[8] = (b[8] & 0x3f) | 0x80 // variant 10
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}

func main() {
	cfg, err := parseArgs(os.Args[1:])
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		fmt.Fprintln(os.Stderr, usage)
		os.Exit(1)
	}

	s := &service{cfg: cfg, handlerID: newUUID(), client: &http.Client{Timeout: 5 * time.Second}}
	log.Printf("VillageOS %s microservice — port %d, mycelium %s, auth=%t",
		serviceName, cfg.Port, cfg.MyceliumURL, cfg.SigningKey != "")

	mux := http.NewServeMux()
	mux.HandleFunc("POST /handle", s.requireAuth(s.handleRelationship))
	mux.HandleFunc("POST /demo/write-kinds", s.requireAuth(s.demoWriteKinds))
	mux.HandleFunc("GET /health", s.health)
	mux.HandleFunc("GET /stats", s.stats)

	srv := &http.Server{Addr: fmt.Sprintf("localhost:%d", cfg.Port), Handler: mux}

	mux.HandleFunc("POST /shutdown", s.requireAuth(func(w http.ResponseWriter, _ *http.Request) {
		writeJSON(w, http.StatusOK, map[string]any{"message": "Shutting down " + serviceName + " microservice"})
		go func() {
			time.Sleep(300 * time.Millisecond)
			_ = srv.Shutdown(context.Background())
		}()
	}))

	go func() {
		time.Sleep(200 * time.Millisecond)
		if err := s.register(); err != nil {
			log.Printf("registration failed: %v", err)
		} else {
			log.Printf("registered with mycelium as %s", s.handlerID)
		}
	}()

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, syscall.SIGINT, syscall.SIGTERM)
	go func() {
		<-stop
		log.Printf("shutting down — processed %d request(s)", s.requests.Load())
		s.deregister()
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = srv.Shutdown(ctx)
	}()

	if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		log.Fatalf("server error: %v", err)
	}
	s.deregister() // covers the /shutdown path
	log.Printf("stopped")
}
