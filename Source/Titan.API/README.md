# Titan.API

## Overview
**Titan.API** serves as the public-facing gateway for the Titan backend infrastructure. It is an ASP.NET Core application responsible for handling real-time client communication via SignalR (WebSockets) and acting as the entry point into the Orleans distributed grain cluster.

## Role in Global Solution
In the Titan architecture, the API layer sits between the client applications (e.g., Unreal Engine game client) and the backend logic (Orleans Silos). It doesn't execute complex domain logic itself but instead forwards requests to the appropriate Grains and streams updates back to connected clients.

## Key Responsibilities
- **SignalR Gateway**: Exposes WebSocket Hubs (`AuthHub`, `CharacterHub`, `InventoryHub`, `TradeHub`, `ItemTypeHub`, `SeasonHub`) for real-time bidirectional communication.
- **Orleans Client**: Connects to the Orleans cluster to invoke grains and query state.
- **Authentication & Authorization**: Validates JWT tokens and enforces access control on Hub methods.
- **Event Streaming**: Subscribes to Orleans streams (e.g., Trade events) to push real-time updates to relevant connected clients.
- **Mock Services**: Provides mock implementations for specialized services (like `IAuthService`) to facilitate development and testing.

## Technologies
- **ASP.NET Core 9**: Web framework.
- **SignalR**: Real-time communication library.
- **Microsoft.Orleans.Client**: Client access to the Orleans cluster.
- **Redis**: Used for clustering coordination (via Aspire).
- **Serilog**: Structured logging.
