# Titan

> Distributed Game Backend built with **[Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/)** and **[.NET 10](https://dotnet.microsoft.com/)**.

Titan is a high-performance, scalable backend solution designed for modern multiplayer games. It creates a "Global Server" architecture where game state (Players, Inventories, Guilds) lives in a distributed mesh of **[Grains](https://learn.microsoft.com/dotnet/orleans/grains/)**.

## Features

- **Distributed State**: Persistent player data using Orleans Grains.
- **Real-Time Communication**: Full duplex communication via **SignalR** Hubs.
- **Inventory System**: Transactional item management.
- **Trading**: Secure peer-to-peer item trading.
- **Identity**: User profiles and social provider linking (Steam, EOS).
- **Security**: JWT Authentication and Role-based Access Control (RBAC).
- **CockroachDB Persistence**: Distributed SQL storage with TLS encryption.
- **.NET Aspire**: Cloud-native orchestration for local development and deployment.

## Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (Desktop or Engine)

### 1-Click Environment Setup (Aspire)
We recommend using **.NET Aspire** to run the solution. It automatically provisions Redis, CockroachDB, and starts all services (API + All Silos) with the correct configuration.

1. Open `Titan.sln` in Visual Studio or Rider.
2. Set **`Titan.AppHost`** as the Startup Project.
3. Press **Start** (F5).

Your browser will open the **Aspire Dashboard**, allowing you to view running services, logs, and metrics.

### Legacy Scripts
Alternatively, use our automation scripts to start the database and run services manually:

```powershell
# Start CockroachDB
.\scripts\docker-up.ps1

# Start all services
.\scripts\run-all.ps1
```

## Testing

Titan includes a full integration test suite.

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
- [x] **CockroachDB Persistence** - Distributed SQL storage with TLS encryption
- [x] **Integration Tests** - Comprehensive test suite with database and clustering tests
- [x] **Metrics/Observability** - OpenTelemetry + Aspire Dashboard
- [ ] **Admin Dashboard** - Web UI for managing players, banning, economy monitoring

### Security & Anti-Cheat
- [x] **Input Validation** - Server-side verification of game actions (Registry/Rules)
- [ ] **Cheat Detection** - Anomaly detection, stat validation

### Client Integration
- [ ] **Client SDK** - C++, C#, and Blueprint support

## Documentation

Full documentation is available at [titan.nexusbound.xyz](https://titan.nexusbound.xyz).
To run the documentation locally:

```bash
cd docs
npm install
npm start
```
