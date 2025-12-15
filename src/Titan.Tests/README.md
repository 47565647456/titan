# Titan.Tests

Unit and Integration tests for the Titan backend logic.

## Test Strategy
These tests use the `Orleans.TestingHost` to spin up an in-memory **TestCluster**. This allows testing Grains in a realistic distributed environment without needing full external infrastructure (Docker).

The `TestSiloConfigurator` configures all required storage providers:
- **OrleansStorage**: Default grain persistence
- **TransactionStore**: Orleans transactions
- **GlobalStorage**: Shared state (seasons, trades)

## Key Test Areas

### Inventory & Items
- `CharacterInventoryGrainTests.cs`: Stacking logic, capacity limits.
- `BaseTypeRegistryTests.cs`: Validation of item definitions.
- `AccountStashGrainTests.cs`: Stash logic.
- `ItemGeneratorTests.cs`: RNG and item creation logic.

### Seasons & Leagues
- `SeasonTests.cs`: Season lifecycle, SSF/Hardcore restrictions.
- `VoidLeagueTests.cs`: Void league mechanics (no migration on death).
- `SeasonMigrationTests.cs`: Character migration logic between seasons.

### Social & Infrastructure
- `SocialGraphTests.cs`: Friend lists and social interactions.
- `PlayerPresenceGrainTests.cs`: Online status tracking.
- `SeedDataLoadingTests.cs`: Verification of initial data seeding.

### Serialization
- `MemoryPackSerializationTests.cs`: Roundtrip tests for all `[MemoryPackable]` model types and storage serializer.
- `SerializationBenchmarkTests.cs`: Performance comparison between MemoryPack and System.Text.Json.

### Running Benchmarks
Run benchmarks with detailed output:
```bash
dotnet test --filter "SerializationBenchmarkTests" --logger "console;verbosity=detailed"
```

## Running Tests
Run via `dotnet test` or the Visual Studio Test Explorer.

### Database Testing
By default, tests run using **in-memory** storage providers for speed.
To run tests against a real PostgreSQL instance, set the environment variable:
```powershell
$env:USE_DATABASE="true"
$env:POSTGRES_CONNECTION="Host=localhost;Port=5432;Database=titan;Username=postgres;Password=TitanDevelopmentPassword123!"
dotnet test
```
