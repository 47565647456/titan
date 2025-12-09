# Titan

> Distributed Game Backend for UE5 built with **Microsoft Orleans** and **.NET 10**.

Titan is a high-performance, scalable backend solution designed for Unreal Engine 5 games. It creates a "Global Server" architecture where game state (Players, Inventories, Guilds) lives in a distributed mesh of "Grains".

## Features

- **Distributed State**: Persistent player data using Orleans Grains.
- **Inventory System**: Transactional item management.
- **Trading**: Secure peer-to-peer item trading.
- **Identity**: User profiles and social provider linking.
- **CockroachDB Persistence**: Scalable, consistent SQL storage.

## Quick Start

### Prerequisites
- .NET 10 SDK
- Docker (Desktop or Engine)

### 1-Click Environment Setup
Use our automation scripts to start the database and initialize the schema automatically:

```powershell
# Start CockroachDB and Init Schema
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
# Run In-Memory Testss
.\scripts\test.ps1

# Run Database Persistence Tests
.\scripts\test.ps1 -WithDatabase
```

See the [Testing Guide](https://titan-docs.nexusbound.xyz/testing) for more details.

## Documentation

Full documentation is available at [titan-docs.nexusbound.xyz](https://titan-docs.nexusbound.xyz).
To run the documentation locally:

```bash
cd docs
npm install
npm start
```
