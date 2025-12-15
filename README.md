# Titan

> Distributed Game Backend built with **[Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/)** and **[.NET 10](https://dotnet.microsoft.com/)**.

Titan is a high-performance, scalable backend solution designed for modern multiplayer games. It creates a "Global Server" architecture where game state (Players, Inventories, Guilds) lives in a distributed mesh of **[Grains](https://learn.microsoft.com/dotnet/orleans/grains/)**.

## Features

- **Distributed State**: Persistent player data using Orleans Grains.
- **Real-Time Communication**: Full duplex communication via **SignalR** Hubs.
- **Inventory System**: Transactional item management with stack validation.
- **Trading**: Secure peer-to-peer item trading with atomic swaps.
- **Seasons & Leagues**: Path of Exile-style seasons with "Void League" support (no migration on death).
- **Identity**: User profiles and social provider linking (Steam, EOS).
- **Security**: JWT Authentication and Role-based Access Control (RBAC).
- **Database Persistence**: PostgreSQL or CockroachDB support with retrying logic for transient errors.
- **High Performance**: **MemoryPack** serialization for wire and storage (~40% smaller payloads).
- **.NET Aspire**: Cloud-native orchestration for local development and deployment.

## Code Organization

The solution is split into specialized micro-services and libraries:

| Project | Description |
|---------|-------------|
| **Titan.AppHost** | .NET Aspire orchestrator. Validates configuration and manages containers (Redis, PostgreSQL/CockroachDB). |
| **Titan.API** | The public gateway. Hosts SignalR Hubs and HTTP endpoints. Acts as an Orleans Client. |
| **Titan.Grains** | Core business logic (Inventory, Trading, Identity) implemented as Orleans Grains. |
| **Titan.Abstractions** | Shared contracts, interfaces, and models used by Clients and Grains. |
| **Titan.IdentityHost** | Dedicated Silo for Identity, Social, and Metadata grains. |
| **Titan.InventoryHost** | Dedicated Silo for Inventory and Item-related grains. |
| **Titan.TradingHost** | Dedicated Silo for Trade management and high-concurrency operations. |
| **Titan.Client** | Strongly-typed .NET Client SDK for connecting to the API. |
| **Titan.Dashboard** | Blazor Server admin interface for managing the game state. |

## Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (Desktop or Engine)

### 1-Click Environment Setup (Aspire)
We recommend using **.NET Aspire** to run the solution. It automatically provisions Redis, PostgreSQL (or CockroachDB), and starts all services (API + All Silos) with the correct configuration.

1. Open `Titan.sln` in Visual Studio or Rider.
2. Set **`Titan.AppHost`** as the Startup Project.
3. Press **Start** (F5).

Your browser will open the **Aspire Dashboard**, allowing you to view running services, logs, and metrics.


## Testing

Titan includes a full integration test suite.
Make sure to trust the .ASP NET HTTPS Development Certificate.

```powershell
# Run In-Memory Tests (Grain Logic)
dotnet test Source/Titan.Tests

# Run End-to-End Tests (Aspire + Docker)
dotnet test Source/Titan.AppHost.Tests
```

## Roadmap

### Core Game Backend
- [x] **Inventory System** - Transactional item management with stack validation
- [x] **Item Registry** - Admin management for item type definitions
- [x] **Item History** - Full audit trail and provenance tracking
- [x] **Trading System** - Peer-to-peer trades with expiration, batch ops, real-time updates
- [x] **Seasons System** - PoE-style leagues with character migration and player-chosen restrictions
- [x] **Account/Character Split** - Global accounts with per-season characters (compound keys)
- [x] **Player Restrictions** - Hardcore (permadeath) and Solo Self-Found (via Rule Engine)
- [ ] **Guilds/Clans** - Player organizations with roles, permissions, shared banks
- [ ] **Currency System** - Virtual currencies, transactions, wallets
- [ ] **Economy Controls** - Price floors/ceilings, taxes, anti-inflation mechanics
- [ ] **Achievements** - Player progression tracking, unlocks, rewards
- [ ] **Quests/Missions** - Task tracking, objectives, rewards distribution
- [ ] **Leaderboards** - Global/regional rankings, seasonal resets

### Identity & Social
- [x] **Identity & Profiles** - User profiles with social provider linking (Steam, EOS)
- [x] **Social Graph** - Friends, blocks, and relationship management
- [ ] **Chat System** - Global, guild, party, whisper channels
- [ ] **Notifications** - Push notifications, in-game alerts, friend online status

### Multiplayer Infrastructure
- [ ] **Matchmaking** - Queue management, skill-based matching, lobby systems
- [ ] **Session/Lobby System** - Game session lifecycle, player slots, ready checks

### API & Real-time
- [x] **WebSocket API** - SignalR hubs with JWT authentication for all game operations
- [x] **Security Hardening** - Role-based authorization, connection shielding, and secure token handling
- [x] **Real-time Events** - Bidirectional communication via SignalR
- [x] **Rate Limiting** - API throttling, service shielding

### Persistence & Operations
- [x] **PostgreSQL/CockroachDB Persistence** - Flexible database backend with retry logic for transient errors
- [x] **Integration Tests** - Comprehensive test suite with database and clustering tests
- [x] **Metrics/Observability** - OpenTelemetry + Aspire Dashboard
- [x] **MemoryPack Wire Serialization** - High-performance binary serialization for Orleans RPC
- [x] **MemoryPack Storage Serialization** - Binary serialization for grain state persistence (~40% smaller payloads)
- [ ] **Admin Dashboard** - Web UI for managing players, banning, economy monitoring

### Security
- [ ] **Input Validation** - Server-side verification of game actions (Registry/Rules)
- [ ] **Grain-Level Authorization** - `ClaimsPrincipal` propagation to Orleans grains (pending [ManagedCode.Orleans.Identity](https://github.com/managed-code-hub/Orleans.Identity) .NET 10 support)

### Client Integration
- [x] **Typed Client SDK** - Compile-time safe SignalR hub access via `TypedSignalR.Client`
- [x] **HTTP Authentication** - REST endpoints for login/logout/refresh
- [ ] **Unreal Plugin** - C++ and Blueprint client library for Unreal Engine

## Documentation

Full documentation is available at [titan.nexusbound.xyz](https://titan.nexusbound.xyz).
To run the documentation locally:

```bash
cd docs
npm install
npm start
```
