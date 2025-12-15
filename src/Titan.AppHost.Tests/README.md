# Titan.AppHost.Tests

End-to-End Integration tests for the full Titan System.

## Test Strategy
These tests use `Aspire.Hosting.Testing` to launch the **entire application stack** (AppHost, API, Silos, Redis, PostgreSQL) as a real running environment.

The test fixture uses `DistributedApplicationTestingBuilder.CreateAsync<T>()` to build and start the AppHost, with configuration passed via command-line arguments for test isolation.

### UserSession Pattern
Tests use the `UserSession` class to efficiently manage SignalR hub connections:
- **One connection per hub type per user** - connections are lazily created and reused
- **Automatic disposal** - implements `IAsyncDisposable` for clean resource management
- Factory methods: `CreateUserSessionAsync()` and `CreateAdminSessionAsync()`

```csharp
await using var user = await CreateUserSessionAsync();
var accountHub = await user.GetAccountHubAsync();
var invHub = await user.GetInventoryHubAsync();  // Reuses same session
// Connections auto-disposed at end of scope
```

### Features Tested
- **Authentication**: Verifies JWT generation, Admin roles, and SignalR connection security (`AuthenticationTests.cs`).
- **Account Management**: Character creation, inventory access, cross-user isolation (`AccountTests.cs`).
- **Admin Operations**: Item type and season creation with role-based authorization (`AdminTests.cs`).
- **Trading**: Full trade lifecycle, atomic item transfers, concurrent trades (`TradingTests.cs`, `TradingFlowIntegrationTests.cs`).
- **Void League**: Season creation, hardcore death handling, migration rules (`VoidLeagueEndToEndTests.cs`).
- **Client SDK**: Validates the `Titan.Client` library against the running API (`TitanClientIntegrationTests.cs`).
- **Concurrency & Clustering**: Verifies system behavior under load and distributed scenarios (`DistributedClusterIntegrationTests.cs`).
- **Connection Tracking**: Verifies online presence and session tracking (`ConnectionTrackingTests.cs`).
- **Infrastructure**: Health checks and silo availability (`ResourceTests.cs`).

## Running Tests
These tests are slower than unit tests as they require spinning up Docker containers and multiple .NET processes.
- Ensure Docker Desktop is running.
- Run via `dotnet test`.
