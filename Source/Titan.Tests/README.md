# Titan.Tests

Unit and Integration tests for the Titan backend logic.

## Test Strategy
These tests use the `Orleans.TestingHost` to spin up an in-memory **TestCluster**. This allows testing Grains in a realistic distributed environment without needing full external infrastructure (Docker).

## Key Test Areas

### Distributed / Clustering
- `DistributedClusterTests.cs`: Verifies behavior across multiple Silos.
  - Sili failures & recovery.
  - Cross-silo trading.
  - Persistence reliability.

### Trading
- `TradeFlowTests.cs`: Happy path and edge cases for trading.
- `TradeTransactionTests.cs`: Verifies ACID properties of item swaps.

### Inventory & Items
- `InventoryTests.cs`: Stacking logic, capacity limits.
- `ItemTypeRegistryTests.cs`: Validation of item definitions.

## Running Tests
Run via `dotnet test` or the Visual Studio Test Explorer.
> **Note**: Some tests marked `[Trait("Category", "Database")]` may require a local Postgres instance (controlled via environment variables).
