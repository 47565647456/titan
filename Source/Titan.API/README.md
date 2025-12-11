# Titan.API

The API Gateway for the Titan backend. It exposes a real-time WebSocket interface for clients (Game Client, Unreal Engine, Web App) to interact with the Orleans cluster.

## Architecture
This project acts as an **Orleans Client**, forwarding requests from SignalR Hubs to the backend Grains. It does not host any Grains itself.

### SignalR Hubs
The API has migrated from HTTP Controllers to SignalR Hubs for full duplex communication.

| Hub | Route | Purpose |
|-----|-------|---------|
| `AuthHub` | `/authHub` | Login (Mock/EOS) and Token generation. |
| `AccountHub` | `/accountHub` | User profile management. |
| `InventoryHub` | `/inventoryHub` | Remote inventory view. |
| `TradeHub` | `/tradeHub` | Real-time trading updates and negotiation. |
| `CharacterHub` | `/characterHub` | Character creation and selection. |
| `ItemTypeHub` | `/itemTypeHub` | Item metadata registry queries. |

### Authentication
The API supports JWT Authentication tailored for game clients.
- **Provider**: `Titan.API.Services.Auth.TokenService`
- **Development**: Supports Mock authentication giving 'Admin' or 'User' roles.
- **Production**: Integrates with Epic Online Services (EOS) Connect. (Configured via `Eos:ClientId`).

### Configuration
Key configuration in `appsettings.json` or Environment Variables:
- `Jwt:Key`: Check `Titan.ServiceDefaults` for validation logic.
- `Cors`: Configurable `AllowedOrigins` for web clients.
- `RateLimiting`: Global limits per IP/User to prevent abuse.
