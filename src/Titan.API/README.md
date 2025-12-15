# Titan.API

The API Gateway for the Titan backend. It serves as the entry point for all clients (Game Client, Unreal Engine, Web App) to interact with the Orleans cluster.

## Architecture

This project acts as an **Orleans Client**, forwarding requests from SignalR Hubs and HTTP endpoints to the backend Grains. It does not host any Grains itself.

### SignalR Hubs (Real-time)
The API uses SignalR Hubs for full duplex communication, primarily for game state and interactions.

| Hub Class | Route | Purpose |
|-----------|-------|---------|
| `AuthHub` | `/authHub` | Token refresh and session management over WebSocket. Login is handled via HTTP. |
| `AccountHub` | `/accountHub` | User profile management. |
| `InventoryHub` | `/inventoryHub` | Remote inventory view. |
| `TradeHub` | `/tradeHub` | Real-time trading updates and negotiation. |
| `CharacterHub` | `/characterHub` | Character creation and selection. |
| `BaseTypeHub` | `/baseTypeHub` | Item metadata registry queries and management (Admin). |
| `SeasonHub` | `/seasonHub` | Season management and information. |

### HTTP Endpoints (Authentication)
Standard HTTP endpoints are used for initial authentication and token retrieval.
- **Base Route**: `/api/auth`
- **Endpoints**:
    - `POST /login`: Authenticate with provider token (EOS or Mock).
    - `POST /refresh`: Refresh access token.
    - `POST /logout`: Revoke refresh token.
    - `GET /providers`: List available providers.
- **Development**: Supports Mock authentication (`mock:{userId}` or `mock:admin:{userId}`).
- **Production**: Integrates with Epic Online Services (EOS) Connect.

### Authentication
The API supports JWT Authentication tailored for game clients.
- **Provider**: `Titan.API.Services.Auth.TokenService`
- **Configuration**:
    - **Development**: Supports Mock authentication giving 'Admin' or 'User' roles.
    - **Production**: Configured via `Eos:ClientId`.

### Configuration
Key configuration in `appsettings.json` or Environment Variables:
- `Jwt:Key`: **Required**. Shared secret for token signing (min 32 chars).
- `Jwt:Issuer`: User for token verification (default: "Titan").
- `Jwt:Audience`: Audience for token verification (default: "Titan").
- `Eos:ClientId`: **Required in Production**. EOS Client ID.
- `Cors:AllowedOrigins`: Array of allowed origins for web clients.
- `RateLimiting`: Global limits.
    - `PermitLimit`: Requests per window (default: 100).
    - `WindowMinutes`: Window size in minutes (default: 1).
- `Sentry:Dsn`: Optional. Enables Sentry error tracking.
- `Redis`: Connection string for Orleans clustering (configured via Aspire service discovery).
