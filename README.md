# Titan

> Distributed Game Backend built with **[Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/)** and **[.NET 10](https://dotnet.microsoft.com/)**.

Titan is a high-performance, scalable backend solution designed for modern multiplayer games. It creates a "Global Server" architecture where game state (Players, Inventories, Guilds) lives in a distributed mesh of **[Grains](https://learn.microsoft.com/dotnet/orleans/grains/)**.

## Features

- **Distributed State**: Persistent player data using Orleans Grains.
- **Inventory System**: Transactional item management.
- **Trading**: Secure peer-to-peer item trading.
- **Identity**: User profiles and social provider linking.
- **[YugabyteDB](https://www.yugabyte.com/) Persistence**: Scalable, PostgreSQL-compatible distributed SQL storage.

## Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (Desktop or Engine)

### 1-Click Environment Setup
Use our automation scripts to start the database and initialize the schema automatically:

```powershell
# Start YugabyteDB and Init Schema
.\scripts\docker-up.ps1
```

### Build & Run

```powershell
# Restore & Build
.\scripts\build.ps1

# Start all services
.\scripts\run-all.ps1
```

## Testing

Titan includes a full integration test suite.

```powershell
# Run In-Memory Tests
.\scripts\test.ps1

# Run Database Persistence Tests
.\scripts\test.ps1 -WithDatabase
```

See the [Testing Guide](https://titan-docs.nexusbound.xyz/testing) for more details.

## Roadmap

### Core Game Backend
- [x] **Inventory System** - Transactional item management with stack validation
- [x] **Item Registry** - Admin API for item type definitions with SignalR notifications
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
- [x] **Identity & Profiles** - User profiles with social provider linking
- [x] **Social Graph** - Friends, blocks, and relationship management
- [ ] **Chat System** - Global, guild, party, whisper channels
- [ ] **Notifications** - Push notifications, in-game alerts, friend online status

### Multiplayer Infrastructure
- [ ] **Matchmaking** - Queue management, skill-based matching, lobby systems
- [ ] **Session/Lobby System** - Game session lifecycle, player slots, ready checks

### API & Real-time
- [x] **REST API** - Controllers with JWT authentication
- [x] **Real-time Events** - SignalR hubs for trades and item type changes
- [ ] **Rate Limiting** - API throttling, abuse prevention

### Persistence & Operations
- [x] **YugabyteDB Persistence** - Scalable SQL storage with Orleans integration
- [x] **Integration Tests** - Comprehensive test suite with database and clustering tests
- [ ] **Metrics/Observability** - Prometheus, Grafana dashboards, grain activation metrics
- [ ] **Admin Dashboard** - Web UI for managing players, banning, economy monitoring

### Security & Anti-Cheat
- [ ] **Input Validation** - Server-side verification of game actions
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
