# VillageOS API Reference

The full REST API and SignalR hub reference lives in the VillageOS broker repo:

- **[Broker Guide](https://dev.azure.com/ReGenVillages/VillageOS/_wiki/wikis/VillageOS-Wiki/9/Broker)** — REST endpoints, authentication, seed loading, SignalR `/vosHub` events
- **Swagger UI** — available at `/swagger` when the broker is running (`https://localhost:7243/swagger`)

## Quick Links

| Area | Endpoint | Method |
|------|----------|--------|
| Login | `/api/auth/login` | POST |
| API Key exchange | `/api/auth/token` | POST (X-API-Key header) |
| Session restore | `/api/auth/restore-session` | GET (HttpOnly cookie) |
| Things | `/api/things` | GET, POST, DELETE |
| Properties | `/api/properties` | GET, PUT, DELETE |
| Relationships | `/api/relationships` | GET, POST, DELETE |
| SignalR Hub | `/vosHub` | WebSocket |
