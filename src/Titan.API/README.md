# Titan.API

The API Gateway for the Titan backend. It serves as the entry point for all clients (Game Client, Unreal Engine, Web App) to interact with the Orleans cluster.

## Architecture

This project acts as an **Orleans Client**, forwarding requests from SignalR Hubs and HTTP endpoints to the backend Grains. It does not host any Grains itself.

### SignalR Hubs (Real-time)
The API uses SignalR Hubs for full duplex communication, primarily for game state and interactions.

| Hub Class | Route | Purpose |
|-----------|-------|---------|
| `AuthHub` | `/authHub` | Session management over WebSocket. Login is handled via HTTP. |
| `AccountHub` | `/accountHub` | User profile management. |
| `InventoryHub` | `/inventoryHub` | Remote inventory view. |
| `TradeHub` | `/tradeHub` | Real-time trading updates and negotiation. |
| `CharacterHub` | `/characterHub` | Character creation and selection. |
| `BaseTypeHub` | `/baseTypeHub` | Item metadata registry (defined in `ItemTypeHub.cs`). |
| `SeasonHub` | `/seasonHub` | Season management and information. |
| `AdminMetricsHub` | `/hubs/admin-metrics` | Real-time rate limiting and system metrics for Admin Dashboard. |

### HTTP Endpoints (Authentication & Admin)
The API uses REST principles for stateless operations like authentication and administrative management.
- **Authentication Route**: `/api/auth`
    - `POST /login`: Authenticate with provider token (EOS or Mock).
    - `POST /logout`: Invalidate the current session.
    - `POST /logout-all`: Invalidate all sessions for the user.
    - `GET /providers`: List available providers.
- **Admin Routes**: `/api/admin`
    - `/api/admin/auth`: Admin dashboard authentication.
    - `/api/admin/users`: User management for admins.
    - `/api/admin/ratelimit`: Dynamics management of rate limiting policies.

### Authentication
The API uses a **Redis-backed Session Ticket** system.
- **Provider**: `Titan.API.Services.Auth.RedisSessionService`
- **Configuration**:
    - **Development**: Supports Mock auth (prefix `mock:guid` or `mock:admin:guid`).
    - **Production**: Validates identities via `EosConnectService` using Epic Online Services.

### Configuration
Key configuration in `appsettings.json` or Environment Variables:
- `Eos:ClientId`: **Required in Production**. EOS Client ID.
- `Cors:AllowedOrigins`: Array of allowed origins for web clients.
- `RateLimiting`: Flexible policy-based limiting.
    - `DefaultPolicies`: Named sets of rules (e.g., "Global", "Auth", "Admin").
    - `DefaultEndpointMappings`: Maps routes to specific policies.
- `Sentry:Dsn`: Optional. Enables Sentry error tracking.
- `Redis`: Keyed services `orleans-clustering` and `rate-limiting` used for state and coordination.
