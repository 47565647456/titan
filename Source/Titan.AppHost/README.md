# Titan.AppHost

The **Aspire** Orchestrator for the Titan Backend. This project is the entry point for running the entire distributed system locally.

## Responsibilities
- **Orchestration**: Starts and manages all services (API, IdentityHost, InventoryHost, TradingHost).
- **Service Discovery**: Injects connection strings and endpoints into child projects.
- **Infrastructure**: Provisions Docker containers for Redis and PostgreSQL.

## Resources

### Databases
- **PostgreSQL (`titan-db`)**:
  - Persists generic Orleans Grain state.
  - Initialized with `init-orleans-db.sql`.
  - **Type**: Extensible via `Database:Type` config (currently only `postgres`).
  - **Password**: Controlled via `postgres-password` parameter.
  - **Persistence**: Controlled via `Database:Volume` config (default: `titan-postgres-data-{env}`).

### Caching / Clustering
- **Redis (`orleans-clustering`)**: 
  - Used by Orleans for Silo membership (Clustering).
  - Includes RedisInsight for inspection.

## Configuration Parameters

| Parameter | Description | Default (Dev) |
|-----------|-------------|---------------|
| `postgres-password` | Password for the Postgres container | `TitanDevelopmentPassword123!` |
| `Database:Type` | Database backend type | `postgres` |
| `Database:Volume` | Docker volume name. Set to `ephemeral` to wipe DB or `none` for no volume. | `(dynamic)` |

## Running the Project
Set this project as the **Startup Project** in Visual Studio or run `dotnet run` to launch the Aspire Dashboard.
