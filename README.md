# Titan

A distributed game backend built with **Microsoft Orleans** and **.NET 10** for player trading, inventory management, and identity services.

## Features

- 🎮 **Federated Identity** - Steam/Epic Games SSO with account linking
- 📦 **Inventory Management** - Stack-aware item registry with validation
- 🔄 **Real-time Trading** - 2-phase commit trades with timeout handling
- 🏆 **Seasonal Rulesets** - SSF/trade restrictions per season
- 📡 **SignalR Hubs** - Real-time WebSocket notifications

## Tech Stack

| Component | Technology |
|-----------|------------|
| Runtime | .NET 10 |
| Framework | Microsoft Orleans 9.x (Virtual Actors) |
| Orchestration | Microsoft Aspire 13 |
| Database | PostgreSQL / YugabyteDB |
| Clustering | Redis |
| Real-time | SignalR |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/)

### Run with Aspire

```powershell
cd Source/Titan.AppHost
dotnet run
```

This starts the entire stack:
- **Redis** - Orleans silo clustering
- **PostgreSQL** - Grain persistence (with `titan` database)
- **IdentityHost** (×2) - User accounts & authentication
- **InventoryHost** (×2) - Item management
- **TradingHost** (×2) - Trade sessions
- **API** - Gateway with SignalR hubs

Access the **Aspire Dashboard** at the URL shown in the console.

### Run Tests

```powershell
cd Source
dotnet test
```

## Architecture

```
┌─────────────────┐
│   Game Client   │
└────────┬────────┘
         │ SignalR WebSocket
         ▼
┌─────────────────┐
│    Titan.API    │ ◄── Orleans Client
└────────┬────────┘
         │ Orleans RPC
         ▼
┌────────────────────────────────────────┐
│           Orleans Cluster              │
│  ┌────────────┐ ┌────────────┐         │
│  │IdentityHost│ │InventoryHost│       │
│  │ TradingHost│ │   (×2 each) │        │
│  └────────────┘ └────────────┘         │
└────────────────────────────────────────┘
         │
    ┌────┴────┐
    ▼         ▼
┌───────┐ ┌────────────┐
│ Redis │ │ PostgreSQL │
└───────┘ └────────────┘
```

## Documentation

See the [docs](./docs) folder for detailed documentation.

## License

MIT
